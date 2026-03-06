using UnityEngine;
using Types;
using System.Collections.Generic;
using System;

public class VirtualMachine : MonoBehaviour
{
    // TODO go over very range operator and make sure they are correct, the end index is exclusive

    [SerializeField] Lexer _lexer;
    [SerializeField] KeyboardAndScreenEditor _keyboard;
    public Simple Simple;
    [SerializeField] Timer _timer;
    public DiskDrive DiskDrive;

    // ------------------------------------------------------- //

    public byte[] Ram; // entire system memory, 4 kilobytes in size
    public readonly int MemorySize = 5119; // in bytes, default is 5119, because of 0 indexing

    // these values they are not to be changed and hence are not stored in ram
    public readonly byte ScreenWidth = 40;
    public readonly byte ScreenHeight = 25;

    //! all words are stored little-endian, where the low byte is stored in the lower memory address     

    //* ----- MEMORY MAP ----- 

    // ----------------------- 0
    //       System ram
    // ----------------------- 256
    //         Stack
    // ----------------------- 512
    //
    //       Screen ram
    //
    // ----------------------- 1536
    //
    //
    //       Simple ram
    //
    //
    // ----------------------- 5120

    //* 0 (#0000) Computer ram & start of zero page
    // mostly variables stored in c# are placed here, also special
    // hardware registers and sound chips are within this space

    // p values are the variables locations in ram, their actual value can be accessed by indexing ram with the variable name (ReadWordFromRam(pProgramSpaceStart))

    // array of bytes telling where simple logical lines start
    // and their length (1 or 2 lines)
    public byte SimpleLogicalLines = 64; //* length: 25
    //* 64 - 83

    public byte Timer = 100; // word that counts how long since the computer was turned on or restored

    public byte FreeSimpleBytes = 115; // how much program space is left for simple programs

    public byte LastKeyDown = 118; // holds the value of the last key pressed. It is not cleared when the key is raised
    public byte LastKeyUp = 119; // value of last key released
    public byte KeysHeld = 120; // number of keys being held down
    public byte KeyPressed = 121; // current key pressed, is 0 if none

    // the position of the cursor, indexed from the top right of the screen
    [HideInInspector] public byte CursorPositionX = 128;
    [HideInInspector] public byte CursorPositionY = 129;

    // whether typed keys are reversed or not
    public byte TypeReversed = 130; // bit 1 (00000001)
    // whether the control key is held
    public byte ControlHeld = 130; // bit 2 (00000010)
    // whether the cursor is in quotes mode
    public byte InQuotes = 130; // bit 3 (00000100)

    // reverse - inverts the screen, making the background green and text white. It simply cvhanges the way text is rendered, they are still treated as non reversed characters by the computer
    public byte IsScreenReversed = 132; // bit 1 (00000001)
    // font - changes the active font, either graphics based or text based 
    public byte ActiveCharacterSet = 132; // bit 2 (00000010)
    // scroll - changes if the screen can scroll when the cursor reaches the bottom of the screen
    public byte CanScreenScroll = 132; // bit 3 (00000100) 
    // cursor - enables or disabled the cursor flashing
    public byte CanCursorFlash = 132; // bit 4 (00001000)
    // break - enables or disables the break key from stopping a running program
    public byte CanBreakProgram = 132; // bit 5 (00010000) // TODO deprecated?

    // points to the end of the current input buffer, where the next character will be inserted
    public byte QueryInputPointer = 139; //* default value: 0
    // holds the characters inputted when using the query command
    public byte QueryInputBuffer = 140; //* length: 80
    //* 140 - 220

    // TODO joystick inputs

    // TODO sound chip registers

    // TODO any variables used in other scripts can be placed into ram by making it a property instead of a field

    public word pStackSpaceEnd = 232; //* default value: 256
    public word pScreenSpaceStart = 234; //* default value: 512

    // simple ram pointers (see simple ram map below)
    public word pStringSpaceStart = 236; //* default value: 5119
    public word pStringSpaceEnd = 238; //* default value: 5119
    public word pArraySpaceEnd = 242; //* default value: 1538
    public word pBoolSpaceEnd = 244; //* default value 1538
    public word pVariableSpaceEnd = 246; //* default value: 1538
    public word pProgramSpaceEnd = 248; //* default value: 1538
    public word pProgramSpaceStart = 250; //* default value: 1536

    public byte StackPointer = 255; // * default value: 255
                                    // byte holds the location of the end of the stack. Find it by adding pStackSpaceEnd + stack pointer.

    //* 256 (#0100) stack
    // used for both the GOSUB and FOR simple commands.
    // stack pointer is stored in ram just for fun even though it would normally be a register in the cpu.
    // stack starts at 511 and grows downward

    //* 512 (#0200) screen ram
    // anything within this area will be interpreted as character and displayed on screen, 
    // what a byte represents changes based on the active character set

    // bytes 0 - 127 are default characters within the character set
    // bytes 128 - 255 are the reverse versions of the first 128

    //* 1511 (#05E7) end of screen ram
    // screen space actually uses 1000 bytes of ram, not 1 Kilobyte (1024 bytes), 
    // the remaining space is left empty

    //* 1536 (#0600) simple ram
    // simple program code is stored and interpreted from here

    // High address (5119) 

    // ------------------ StringSpaceStart
    //  String variables
    // ------------------ StringSpaceEnd
    //  Marker variables
    // ------------------ MarkerSpaceEnd
    //
    //
    //    empty space
    //
    //
    // ------------------ ArraySpaceEnd
    //
    //   Array variables
    //
    // ------------------ BoolSpaceEnd
    //   Bool variables
    // ------------------ VariableSpaceEnd
    //     Variables
    // ------------------ ProgramSpaceEnd
    //
    //    Program code
    //
    // ------------------ ProgramSpaceStart

    // Low address (1536)

    //* 5120 (#1400) end of ram


    // ------------------------------------------------------- //

    // called when the computer is turned on / power cycled 
    // wipes everything on the computer and resets
    void Initialise()
    {
        Ram = new byte[5120]; // Initialise 5k of ram, default value of all bytes is 0

        InitaliseVariables();

        Simple.NewProgram(); // wipe program in memory and make a blank one

        // TODO check affected devices such as disk drives
    }

    // only resets computer variables and not the current program in memory
    // the restore key calls this methiod, and not Initialise
    public void InitaliseVariables() // TODO setting simple address pointers shouldnt happen if the program is kept in memory
    {
        //* set address pointers
        WriteWordToRam(pScreenSpaceStart, 512);
        WriteWordToRam(pProgramSpaceStart, 1536);
        WriteWordToRam(pProgramSpaceEnd, 1538);
        WriteWordToRam(pVariableSpaceEnd, 1538);
        WriteWordToRam(pBoolSpaceEnd, 1538);
        WriteWordToRam(pArraySpaceEnd, 1538);

        WriteWordToRam(pStackSpaceEnd, 256);

        // memorysize = top of ram
        WriteWordToRam(pStringSpaceStart, MemorySize);
        WriteWordToRam(pStringSpaceEnd, MemorySize);

        WriteByteToRam(StackPointer, 255); // set stack pointer to beginning (stack empty)

        //* set bit flags
        WriteBitToRam(TypeReversed, 0, false); // text not reversed
        WriteBitToRam(ControlHeld, 1, false); // controlkey not held
        WriteBitToRam(InQuotes, 2, false); // not within quotes

        WriteBitToRam(IsScreenReversed, 0, false); // set screen unreversed (bit 1)
        WriteBitToRam(ActiveCharacterSet, 1, false); // set graphics character set (bit 2)
        _keyboard.TextField.font = _keyboard.GraphicsFont;
        WriteBitToRam(CanScreenScroll, 2, true); // enable screen scrolling (bit 3)
        WriteBitToRam(CanCursorFlash, 3, true); // enable cursor flash (bit 4)
        WriteBitToRam(CanBreakProgram, 4, true); // enable program break (bit 5)

        //* clear simple logical lines
        for (int i = 0; i < ScreenHeight; i++) Ram[SimpleLogicalLines + i] = 0;

        // TODO reinsert at end in new locations and sizes
        // WriteFormattedTextToRam(0, "lmc-77 and simple designed and created by liam latz.");
        // WriteFormattedTextToRam(302, "simple is inspired by microsoft basic; written by bill gates, paul allen, and monte davidoff.");

        _timer.StartTimer();
        CalculateFreeBytes();
    }

    // TODO
    // freshes all components of the computer from their values in ram.
    // moves pointers, refreshes the screen and changes fonts
    // called whenever WRITE is executed, and every so often by the timer
    public void RefreshComputer()
    {
        _keyboard.RefreshScreen();

        // if bit 2 is 0 (false), then graphics font
        if (!ReadBitFromRam(ActiveCharacterSet, 1)) _keyboard.TextField.font = _keyboard.GraphicsFont;
        else _keyboard.TextField.font = _keyboard.GraphicsFont;
    }

    void Awake()
    {
        Initialise();
    }

    //* helper methods

    public byte[] PullBytesFromStack(word length)
    {
        word stackEnd = ReadWordFromRam(pStackSpaceEnd) + Ram[StackPointer]; // end of stack space + stack pointer
        if (stackEnd + length > (ReadWordFromRam(pStackSpaceEnd) + 255)) throw new InterpreterException("04 out of memory error", "stack underflow");

        WriteByteToRam(StackPointer, (byte)(Ram[StackPointer] + length)); // adjust stack pointer

        return Ram[stackEnd..(stackEnd + length)]; // return array from stack // TODO check range operator
    }

    public void PushBytesToStack(byte[] bytes)
    {
        word stackEnd = ReadWordFromRam(pStackSpaceEnd) + Ram[StackPointer]; // end of stack space + stack pointer
        print($"end of stack: {stackEnd}");

        if (stackEnd - bytes.Length < ReadWordFromRam(pStackSpaceEnd)) throw new InterpreterException("04 out of memory error", "stack overflow");

        WriteByteArrayToRam(stackEnd - bytes.Length, bytes);

        WriteByteToRam(StackPointer, (byte)(Ram[StackPointer] - bytes.Length)); // adjust stack pointer
    }

    public bool DoByteArraysMatch(byte[] first, byte[] second)
    {
        string a = "";
        foreach (byte b in first) a += b.ToString() + ",";
        string c = "";
        foreach (byte b in second) c += b.ToString() + ",";

        print($"first: {a}, second: {c}");



        if (first.Length != second.Length) return false;

        for (int i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i]) return false;
        }

        return true;
    }

    public byte[] ScreenTextToByteArray(string screenText)
    {
        byte[] bytes = new byte[screenText.Length];

        for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)Array.IndexOf(CharacterSetValues, screenText[i]);

        return bytes;
    }

    public void CalculateFreeBytes()
    {
        word freeBytes = ReadWordFromRam(pStringSpaceEnd) - ReadWordFromRam(pArraySpaceEnd);
        WriteWordToRam(FreeSimpleBytes, freeBytes);
    }

    public void WriteWordToRam(int LoAddress, int Value)
    {
        word Word = new word(Value);

        WriteByteToRam(LoAddress, Word.Lo);
        WriteByteToRam(LoAddress + 1, Word.Hi);
    }

    public word ReadWordFromRam(int LoAddress)
    {
        // print(LoAddress); // TODO remove this
        word Word = new()
        {
            Lo = Ram[LoAddress],
            Hi = Ram[LoAddress + 1]
        };

        return Word;
    }

    // bit value uses 0 indexing
    public void WriteBitToRam(word address, int bit, bool value)
    {
        byte mask = (byte)(1 << bit); // shifts a bit into the correct place in mask
        byte reverseMask = (byte)(mask ^ 255); // use xor to reverse the bits in the mask

        Ram[address] &= reverseMask; // clear the value of the bit
        if (value) Ram[address] |= mask; // if value is true, write the bit at the address, otherwise it is already cleared

        if (address > ReadWordFromRam(pScreenSpaceStart) && address < ReadWordFromRam(pScreenSpaceStart) + 1000) _keyboard.RefreshScreen(); ;
    }

    // bit value uses 0 indexing
    public bool ReadBitFromRam(word address, int bit) => (Ram[address] & (byte)Math.Pow(2, bit)) == Math.Pow(2, bit);

    // checks if the byte is witin screen space and if it is, writes to the screen. 
    // Should be used over indexing to Ram when characters aren't expected to be printed but is a possibility
    public void WriteByteToRam(word address, byte value)
    {
        Ram[address] = value;
        if (address >= ReadWordFromRam(pScreenSpaceStart) && address < ReadWordFromRam(pScreenSpaceStart) + 1000) _keyboard.TextField.text = _keyboard.TextField.text.Remove(address - ReadWordFromRam(pScreenSpaceStart), 1).Insert(address - ReadWordFromRam(pScreenSpaceStart), CharacterSetValues[value].ToString());
    }

    // alternative to Array.Copy 
    public void WriteByteArrayToRam(word address, byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++) WriteByteToRam(address + i, bytes[i]);
    }

    public void WriteFormattedTextToRam(word address, string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            Ram[address + i] = (byte)Array.IndexOf(CharacterSetValues, (char)(text[i] + 224)); // writes character into ram shifted to screen text
        }
    }

    // byte values for all of the characters within the characterset. use Array.indexOf(CharacterSetValues, 'char') to get its value
    //? this array breaks if not made readonly (which is fine because it is readonly)
    public readonly char[] CharacterSetValues =
    {
        // non reversed (0-127)
        'Ā', 'ā', 'Ă', 'ă', 'Ą', 'ą', 'Ć', 'ć', 'Ĉ', 'ĉ', 'Ċ', 'ċ', 'Č', 'č', 'Ď', 'ď',
        'Đ', 'đ', 'Ē', 'ē', 'Ĕ', 'ĕ', 'Ė', 'ė', 'Ę', 'ę', 'Ě', 'ě', 'Ĝ', 'ĝ', 'Ğ', 'ğ',
        'Ġ', 'ġ', 'Ģ', 'ģ', 'Ĥ', 'ĥ', 'Ħ', 'ħ', 'Ĩ', 'ĩ', 'Ī', 'ī', 'Ĭ', 'ĭ', 'Į', 'į',
        'İ', 'ı', 'Ĳ', 'ĳ', 'Ĵ', 'ĵ', 'Ķ', 'ķ', 'ĸ', 'Ĺ', 'ĺ', 'Ļ', 'ļ', 'Ľ', 'ľ', 'Ŀ',
        'ŀ', 'Ł', 'ł', 'Ń', 'ń', 'Ņ', 'ņ', 'Ň', 'ň', 'ŉ', 'Ŋ', 'ŋ', 'Ō', 'ō', 'Ŏ', 'ŏ',
        'Ő', 'ő', 'Œ', 'œ', 'Ŕ', 'ŕ', 'Ŗ', 'ŗ', 'Ř', 'ř', 'Ś', 'ś', 'Ŝ', 'ŝ', 'Ş', 'ş',
        'Ʊ', 'Ʒ', 'ƥ', 'Ʋ', 'ƴ', 'ƹ', 'Ƶ', 'Ʃ', 'Ư', 'ư', 'ƛ', 'Ɲ', 'ơ', 'Ƴ', 'Ƥ', 'Ʀ',
        'Ƨ', 'ƨ', 'ƪ', 'ƫ', 'Ƭ', 'ƺ', 'Ƹ', 'ƣ', 'ƶ', 'Ƣ', 'Ʈ', 'ƭ', 'Ɯ', 'ŭ', 'Ā', 'Ā', 
        
        // reversed (128-255)
        'ǀ', 'ǁ', 'ǂ', 'ǃ', 'Ǆ', 'ǅ', 'ǆ', 'Ǉ', 'ǈ', 'ǉ', 'Ǌ', 'ǋ', 'ǌ', 'Ǎ', 'ǎ', 'Ǐ',
        'ǐ', 'Ǒ', 'ǒ', 'Ǔ', 'ǔ', 'Ǖ', 'ǖ', 'Ǘ', 'ǘ', 'Ǚ', 'ǚ', 'Ǜ', 'ǜ', 'ǝ', 'Ǟ', 'ǟ',
        'Ǡ', 'ǡ', 'Ǣ', 'ǣ', 'Ǥ', 'ǥ', 'Ǧ', 'ǧ', 'Ǩ', 'ǩ', 'Ǫ', 'Ǭ', 'ǫ', 'ǭ', 'Ǯ', 'ǯ',
        'ǰ', 'Ǳ', 'ǲ', 'ǳ', 'Ǵ', 'ǵ', 'Ƕ', 'Ƿ', 'Ǹ', 'ǹ', 'Ǻ', 'ǻ', 'Ǽ', 'ǽ', 'Ǿ', 'ǿ',
        'Ȁ', 'ȁ', 'Ȃ', 'ȃ', 'Ȅ', 'ȅ', 'Ȇ', 'ȇ', 'Ȉ', 'ȉ', 'Ȋ', 'ȋ', 'Ȍ', 'ȍ', 'Ȏ', 'ȏ',
        'Ȑ', 'ȑ', 'Ȓ', 'ȓ', 'Ȕ', 'ȕ', 'Ȗ', 'ȗ', 'Ș', 'ș', 'Ț', 'ț', 'Ȝ', 'ȝ', 'Ȟ', 'ȟ',
        'ɱ', 'ɷ', 'ɥ', 'ɲ', 'ɴ', 'ɹ', 'ɵ', 'ɩ', 'ɯ', 'ɰ', 'ɛ', 'ɝ', 'ɡ', 'ɳ', 'ɤ', 'ɦ',
        'ɧ', 'ɨ', 'ɪ', 'ɫ', 'ɬ', 'ɺ', 'ɸ', 'ɣ', 'ɶ', 'ɢ', 'ɮ', 'ɭ', 'ɜ', 'ȭ', 'ǀ', 'ǀ'
    };
}

// holds the word struct, include "using Types;" to allow use of the word type
namespace Types
{
    public struct word // lowercase to emulate a data type
    {
        public ushort Value;

        public byte Hi
        {
            get { return (byte)(Value >> 8); } // push high byte down to lower
            set
            {
                ushort Upper = (ushort)(value << 8); // push byte to upper byte

                ushort Lower = (ushort)(Value & 0x00FF); // delete the upper byte of the current value

                Value = (ushort)(Upper | Lower); // add upper byte to current value
            }
        }

        public byte Lo
        {
            get { return (byte)(Value & 0x00FF); } // delete high byte
            set
            {
                ushort Lower = value; // already lower byte

                ushort Upper = (ushort)(Value & 0xFF00); // delete the lower byte of the current value

                Value = (ushort)(Upper | Lower); // add the lower byte to the current value
            }
        }

        public word(int value)
        {
            Value = (ushort)value;
        }

        public word(byte lo, byte hi)
        {
            Value = 0;
            Lo = lo;
            Hi = hi;
        }

        // casting to and from integers, allows "word X = 5;", and "int Y = X;"
        public static implicit operator int(word Word) => Word.Value;
        public static implicit operator word(int Int) => new word(Int);

        // casting to an array indexer, allows for "array[X]" where X is a word
        public static implicit operator Index(word Word) => Word.Value;

        // prints the structs value instead of its name
        public override string ToString() => $"{Value}";
    }
}
