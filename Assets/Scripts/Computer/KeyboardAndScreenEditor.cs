using System;
using System.Collections;
using TMPro;
using UnityEngine;
using Types;
using System.Collections.Generic;
using System.IO;

public class KeyboardAndScreenEditor : MonoBehaviour
{
    [SerializeField] VirtualMachine _vm;
    [SerializeField] CursorPosition _cursorPosition;
    [SerializeField] Tokeniser _tokeniser;
    [SerializeField] Interpreter _interpreter;

    public TMP_Text TextField;
    public TMP_FontAsset GraphicsFont;
    [SerializeField] TMP_FontAsset _textFont;

    public const char Space = 'Ā';
    public const char ReversedSpace = 'ǀ';
    public const char Quote = 'Ă';

    public const char ReverseOnControlCharacter = 'Ȓ';
    public const char ReverseOffControlCharacter = 'ɝ';
    public const char RestoreControlCharacter = 'Ǡ';
    public const char HomeControlCharacter = 'ǡ';
    public const char ClearControlCharacter = 'Ǹ';
    public const char FontControlCharacter = 'Ǧ';

    public const char UpControlCharacter = 'Ǿ';
    public const char DownControlCharacter = 'Ȗ';
    public const char LeftControlCharacter = 'ǜ';
    public const char RightControlCharacter = 'Ǟ';






    //* Bit flags

    private bool IsTextFontActive
    {
        get => _vm.ReadBitFromRam(_vm.ActiveCharacterSet, 1); // returns value of bit 2, 1 is text, 0 is graphics
        set => _vm.WriteBitToRam(_vm.ActiveCharacterSet, 1, value); // write the value to the address
    }

    private bool CanScrollScreen
    {
        get => _vm.ReadBitFromRam(_vm.CanScreenScroll, 2); // return value of bit 3
        set => _vm.WriteBitToRam(_vm.CanScreenScroll, 2, value); // write the value to the address
    }

    private bool CanFlashCursor
    {
        get => _vm.ReadBitFromRam(_vm.CanCursorFlash, 3); // returns value of bit 4
    }

    // seperate method since it has the side effect of enabling or disabling the cursor
    public void SetCanFlashCursor(bool value)
    {
        _vm.WriteBitToRam(_vm.CanCursorFlash, 3, value);

        if (value) _cursorPosition.EnableCursor();
        else _cursorPosition.DisableCursor();
        // TODO make cusor invisible if its position is no longer being updated
    }

    private byte XPos
    {
        get => _vm.Ram[_vm.CursorPositionX];
        set => _vm.WriteByteToRam(_vm.CursorPositionX, value);
    }

    private byte YPos
    {
        get => _vm.Ram[_vm.CursorPositionY];
        set => _vm.WriteByteToRam(_vm.CursorPositionY, value);
    }

    private bool Reverse // characters types are reversed
    {
        get => _vm.ReadBitFromRam(_vm.TypeReversed, 0);
        set => _vm.WriteBitToRam(_vm.TypeReversed, 0, value);
    }

    public bool ControlHeld // control key is held
    {
        get => _vm.ReadBitFromRam(_vm.ControlHeld, 1);
        set => _vm.WriteBitToRam(_vm.ControlHeld, 1, value);
    }

    private bool InQuotes // if the user is typing within quotes (control codes are inserted as character instead of executed)
    {
        get => _vm.ReadBitFromRam(_vm.InQuotes, 2);
        set => _vm.WriteBitToRam(_vm.InQuotes, 2, value);
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created

    void Awake()
    {
        // fill the textbox with blank characters
        for (int i = 0; i < _vm.ScreenWidth * _vm.ScreenHeight; i++) TextField.text += Space.ToString();
    }

    void Start()
    {
        // display opening screen 
        StartCoroutine(Startup());
    }

    // TODO with shorthands, a line can be listed longer than two lines, make all lines longer than 2 a 1 logical line

    void OnEnable()
    {
        LMCInputManager.CursorLeft += CursorLeft;
        LMCInputManager.CursorRight += CursorRight;
        LMCInputManager.CursorDown += CursorDown;
        LMCInputManager.CursorUp += CursorUp;
        LMCInputManager.TextPressed += TextInput;
        LMCInputManager.BreakPressed += Escape;
    }

    void OnDisable()
    {
        LMCInputManager.CursorLeft -= CursorLeft;
        LMCInputManager.CursorRight -= CursorRight;
        LMCInputManager.CursorDown -= CursorDown;
        LMCInputManager.CursorUp -= CursorUp;
        LMCInputManager.TextPressed -= TextInput;
        LMCInputManager.BreakPressed -= Escape;
    }

    private void Escape()
    {
        // TODO if in program, break

        if (!_vm.ReadBitFromRam(_vm.CanBreakProgram, 4)) return;
    }

    public void BackSpace()
    {
        CursorLeft(false);

        int bindex = (YPos * _vm.ScreenWidth) + XPos;

        // insert character into textbox
        TextField.text = TextField.text.Remove(bindex, 1).Insert(bindex, Space.ToString());

        // insert byte into ram
        _vm.Ram[_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + bindex] = (byte)Array.IndexOf(_vm.CharacterSetValues, Space);
    }

    private void Enter()
    {
        // indexes into the Simple logical lines array with the current row of the cursor
        int rowNumber = _vm.Ram[_vm.SimpleLogicalLines + YPos];

        if (rowNumber == 1 || rowNumber == 2) // in first line of a logical line
        {
            string logicalLine = TextField.text.Substring(_vm.ScreenWidth * YPos, _vm.ScreenWidth * rowNumber); // grab either 1 or 2 lines of the text field
            _tokeniser.TokeniseInputString(logicalLine); // submit line to tokeniser
        }
        else if (rowNumber == 3) // in second line of a logical line
        {
            string logicalLine = TextField.text.Substring(_vm.ScreenWidth * (YPos - 1), _vm.ScreenWidth * 2); // grab the line above and current line of the text field
            print(logicalLine);
            _tokeniser.TokeniseInputString(logicalLine); // submit line to tokeniser
        }
        else // rownumber is 0, line is empty
        {
            CarriageReturn();
        }


        InQuotes = false;
        Reverse = false;

        FlashCursor();
    }

    // determine what shoud happen based on what the inputted character it (control codes, text input, shifting lines)
    private void TextInput(char character)
    {
        if (character.Equals('\b'))
        {
            BackSpace();
            return;
        }

        if (character.Equals('\n') || character.Equals('\r'))
        {
            ClearSimpleLogicalLine(false);
            Enter();
            return;
        }

        if (char.IsControl(character)) return; // all unhandled escape codes are ignored.

        if (character.Equals('\"')) InQuotes = !InQuotes; // enter or exit quotes mode

        // custom control codes (control + number key), essentially function keys
        // will return after executing effect; nothing is printed
        if (ControlHeld)
        {
            switch (character)
            {
                case ' ':
                    Insert();
                    return;
                case '5': // restore computer
                    if (!InQuotes) Restore();
                    else TypeRawCharacter(RestoreControlCharacter);
                    return;
                case '6': // cursor home
                    if (!InQuotes) CursorHome();
                    else TypeRawCharacter(HomeControlCharacter);
                    return;
                case '7': // clear screen
                    if (!InQuotes) ClearScreen();
                    else TypeRawCharacter(ClearControlCharacter);
                    return;
                case '8': // toggle font
                    if (InQuotes)
                    {
                        TypeRawCharacter(FontControlCharacter);
                        return;
                    }

                    // flips text field font
                    if (IsTextFontActive) TextField.font = GraphicsFont;
                    else TextField.font = _textFont;

                    // flips font bit in memory 
                    IsTextFontActive = !IsTextFontActive;
                    FlashCursor();

                    return;
                case '9': // enable reverse characters
                    if (!InQuotes)
                    {
                        Reverse = true;
                        FlashCursor();
                    }
                    else TypeRawCharacter(ReverseOnControlCharacter);
                    return;
                case '0': // disable reverse characters
                    if (!InQuotes)
                    {
                        Reverse = false;
                        FlashCursor();
                    }
                    else TypeRawCharacter(ReverseOffControlCharacter);
                    return;
            }
        }

        TypeCharacter(character);
    }

    // shifts the logical line forward one character at the cursor position
    void Insert()
    {
        int index = (YPos * _vm.ScreenWidth) + XPos;

        char character = Reverse ? ReversedSpace : Space;

        int currentRow = _vm.Ram[_vm.SimpleLogicalLines + YPos];

        if (currentRow == 1) // single line, if pushing over the edge, shift down and create a second line
        {
            int point;

            if (!IsCharacterEmpty((_vm.ScreenWidth * (YPos + 1)) - 2)) // if end of line is not empty
            {
                point = _vm.ScreenWidth * (YPos + 2);
                if (!IsLineEmpty(YPos + 1)) ShiftScreenDown(YPos + 1);

                _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 2);
                _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos + 1, 3);

            }
            else point = _vm.ScreenWidth * (YPos + 1);


            TextField.text = TextField.text.Insert(index, character.ToString());
            TextField.text = TextField.text.Remove(point, 1);

            Array.Copy(_vm.Ram, _vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index, _vm.Ram, _vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index + 1, point - index - 1);
            _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index, 0); // insert space into ram
        }
        else if (currentRow == 2 || currentRow == 3) // already two lines, prevent pushing further than the second line
        {
            int line = currentRow == 2 ? 2 : 1;
            int point = _vm.ScreenWidth * (YPos + line);

            if (TextField.text[point - 1] != Space && TextField.text[point] != ReversedSpace) return;

            TextField.text = TextField.text.Insert(index, character.ToString());
            TextField.text = TextField.text.Remove(point, 1);

            Array.Copy(_vm.Ram, _vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index, _vm.Ram, _vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index + 1, point - index - 1);
            _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index, 0); // insert space into ram
        }
    }

    // insert the character at the current cursor position
    void TypeCharacter(char character)
    {
        int index = (YPos * _vm.ScreenWidth) + XPos;

        char characterToScreenPrint = character;

        // check for graphics character
        if (ControlHeld) characterToScreenPrint = (char)(char.ToLower(characterToScreenPrint) + 224 + 96); // default add 224, then add 96 for control character
        else characterToScreenPrint = (char)(characterToScreenPrint + 224);

        // regardless of control or not, character can still reverse.
        if (Reverse) characterToScreenPrint = (char)(characterToScreenPrint + 128 + 64);

        // insert text
        TextField.text = TextField.text.Remove(index, 1).Insert(index, characterToScreenPrint.ToString());

        // Insert the value of the character into the appropriate screen ram
        _vm.Ram[_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index] = (byte)Array.IndexOf(_vm.CharacterSetValues, characterToScreenPrint);


        // indexes into the Simple logical lines array with the current row of the cursor
        int rowNumber = _vm.Ram[_vm.SimpleLogicalLines + YPos];

        // remove space and reverse space keys

        if (rowNumber == 0) _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 1);

        int previousRow = YPos;

        CursorRight(false);

        if (YPos != previousRow && _vm.Ram[_vm.SimpleLogicalLines + previousRow] == 1) // typed down to new line, and was not already on a second line and the previous line was not cleared
        {
            if (_vm.Ram[_vm.SimpleLogicalLines + YPos] != 0) ShiftScreenDown(YPos); // make room for second line before typing

            _vm.WriteByteToRam(_vm.SimpleLogicalLines + previousRow, 2);
            _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 3);

            return;
        }
    }

    // called when the cursor leaves a line, if it is empty then its logical line is removed from the array
    void ClearSimpleLogicalLine(bool delete)
    {
        if (!IsLineEmpty(YPos)) return;

        int rowNumber = _vm.Ram[_vm.SimpleLogicalLines + YPos];

        if (rowNumber == 2) // top of 2 line logical line 
        {
            //! _vm.Ram[_vm.pSimpleLogicalLines + YPos] = 0; // delete row
            _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 0);

            //* this prevents pushing a filled single line (2-long logical line) into 1 line below, without this, a full line of characters could be treated as 1 logical line
            if (!IsCharacterEmpty(_vm.ScreenWidth * (YPos + 2) - 1)) // if last character of second line is not empty, it should be 2 logical lines
            {
                if (!IsLineEmpty(YPos + 2)) ShiftScreenDown(YPos + 2); // if line below is not empty, shift so lines arent merged

                //! _vm.Ram[_vm.pSimpleLogicalLines + YPos + 1] = 2; // row below is now a top line
                _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos + 1, 2);
                //! _vm.Ram[_vm.pSimpleLogicalLines + YPos + 2] = 3; // row below that 
                _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos + 2, 3);

                return;
            }

            //! _vm.Ram[_vm.pSimpleLogicalLines + YPos + 1] = 1; // bottom row now single
            _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos + 1, 1);

        }
        else if (rowNumber == 3) // bottom of 2-line logical line
        {
            // line is empty but there is a character at the last point of the above line, cannot delete logical line
            // if the delete key was pressed then it can continue as that character will be deleted
            if (!IsCharacterEmpty((_vm.ScreenWidth * YPos) - 1) && !delete) return;

            //! _vm.Ram[_vm.pSimpleLogicalLines + YPos] = 0; // delete row
            _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 0);

            //! _vm.Ram[_vm.pSimpleLogicalLines + YPos - 1] = 1; // top row now single
            _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos - 1, 1);

        }
        else if (rowNumber == 1)
        {
            //! _vm.Ram[_vm.pSimpleLogicalLines + YPos] = 0; // delete row
            _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 0);

        }
    }

    void ShiftScreenDown(int YRow)
    {
        print($"Shiftingscreen down from line {YRow}");
        int cursorIndex = YRow * _vm.ScreenWidth; // start of line, always shifts whole rows down

        TextField.text = TextField.text.Insert(cursorIndex, "ĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀĀ");
        TextField.text = TextField.text.Remove(_vm.ScreenWidth * _vm.ScreenHeight, 40);

        int screenSpaceStart = _vm.ReadWordFromRam(_vm.pScreenSpaceStart);

        // shift screen bytes upwards, screen will be refreshed at the end
        Array.Copy(_vm.Ram, screenSpaceStart + cursorIndex, _vm.Ram, screenSpaceStart + cursorIndex + _vm.ScreenWidth, 40);

        // clear byte row
        for (int x = 0; x < _vm.ScreenWidth; x++) _vm.Ram[screenSpaceStart + cursorIndex + x] = 0;

        // shift section of logical array down 1
        Array.Copy(_vm.Ram, _vm.SimpleLogicalLines + YPos, _vm.Ram, _vm.SimpleLogicalLines + YPos + 1, _vm.ScreenHeight - YPos);
        _vm.Ram[_vm.SimpleLogicalLines + _vm.ScreenHeight] = 0; // clear the byte that was pushed out the bounds of the array

        print(_vm.SimpleLogicalLines + _vm.ScreenHeight);
        if (_vm.Ram[_vm.SimpleLogicalLines + _vm.ScreenHeight - 1] == 2) _vm.WriteByteToRam(_vm.SimpleLogicalLines + _vm.ScreenHeight - 1, 1); // if shifting the screen cut a line in half, make it a single line
        RefreshScreen(); // array.copy may cause multiple moves making idividual corrections unnecessary
    }

    // character is already screen text and does not need to be shifted
    public void TypeRawCharacter(char character)
    {
        int index = (YPos * _vm.ScreenWidth) + XPos;

        // insert text
        TextField.text = TextField.text.Remove(index, 1).Insert(index, character.ToString());

        // Insert the value of the character into the appropriate screen ram
        // indexed from the start of screen space plus the cursor position
        _vm.Ram[_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index] = (byte)Array.IndexOf(_vm.CharacterSetValues, character);

        int rowNumber = _vm.Ram[_vm.SimpleLogicalLines + YPos];

        if (rowNumber == 0) _vm.Ram[_vm.SimpleLogicalLines + YPos] = 1; // if row empty, add logical line

        int previousRow = YPos;

        CursorRight(false);

        if (YPos != previousRow && _vm.Ram[_vm.SimpleLogicalLines + previousRow] == 1) // typed down to new line, and was not already on a second line and the previous line was not cleared
        {
            if (_vm.Ram[_vm.SimpleLogicalLines + YPos] != 0) ShiftScreenDown(YPos); // make room for second line before typing

            _vm.Ram[_vm.SimpleLogicalLines + previousRow] = 2; // last line is now the start of a two-line logical line
            _vm.Ram[_vm.SimpleLogicalLines + YPos] = 3; // new line is second line of logical line

            return;
        }
    }

    //? the "key" parameter on the cursors tells if a cursor key was pressed, 
    // or if typing a character is using the method to shift the cursor right,
    // only the cursor keys should display a control character, not shifting the cursor for typing
    // also cursor keys are not consider logical line breakers while type shifting is

    public void CursorUp(bool _)
    {
        if (InQuotes)
        {
            TypeRawCharacter(UpControlCharacter); // insert control character
            return;
        }

        ClearSimpleLogicalLine(false);


        if (YPos != 0) YPos--;
        FlashCursor();
    }

    public void CursorDown(bool cursor)
    {
        if (InQuotes && cursor)
        {
            TypeRawCharacter(DownControlCharacter); // insert control character
            return;
        }

        ClearSimpleLogicalLine(false);

        // if at bottom of screen, scroll it downwards
        if (YPos != _vm.ScreenHeight - 1) YPos++;
        else if (CanScrollScreen) ScrollScreen();

        FlashCursor();
    }

    public void CursorLeft(bool cursorkeyPressed)
    {
        if (cursorkeyPressed && InQuotes) // insert control character if cursor key was pressed
        {
            TypeRawCharacter(LeftControlCharacter);
            return;
        }

        if (XPos != 0)
        {
            XPos--;
        }
        else // at left side of screen
        {
            if (YPos == 0) return; // top left, cannot wrap around

            if (!cursorkeyPressed) ClearSimpleLogicalLine(true); // delete key is wrapping to above line

            XPos = (byte)(_vm.ScreenWidth - 1);
            YPos--;
        }

        FlashCursor();
    }

    public void CursorRight(bool cursorKeyPressed)
    {
        if (cursorKeyPressed && InQuotes)
        {
            TypeRawCharacter(RightControlCharacter); // insert control character if cursor key was pressed
            return;
        }

        if (XPos != (byte)(_vm.ScreenWidth - 1)) XPos++;
        else // at right side of screen
        {
            if (YPos == _vm.ScreenHeight - 1 && CanScrollScreen) // bottom right of screen, scroll down
            {
                ScrollScreen();
                XPos = 0;
            }
            else // else wrap around
            {
                ClearSimpleLogicalLine(true);
                XPos = 0;
                YPos++;
            }
        }

        FlashCursor();
    }

    public void ClearScreen()
    {
        TextField.text = "";

        // wipe the screen of characters and cleart the bytes in ram
        for (int i = 0; i < _vm.ScreenWidth * _vm.ScreenHeight; i++)
        {
            TextField.text += Space.ToString();
            //! _vm.Ram[_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + i] = 0;
            _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + i, 0);
        }

        // clear simple logical lines
        // for (int i = 0; i < _vm.ScreenHeight; i++) _vm.Ram[_vm.pSimpleLogicalLines + i] = 0;
        for (int i = 0; i < _vm.ScreenHeight; i++) _vm.WriteByteToRam(_vm.SimpleLogicalLines + i, 0);


        CursorHome();
    }

    // bring cursor to top left of the screen
    public void CursorHome()
    {
        ClearSimpleLogicalLine(false);

        XPos = 0;
        YPos = 0;
        FlashCursor();
    }

    // resets pointers and flags but keeps the current program in memory.
    public void Restore()
    {
        _vm.InitaliseVariables();

        ClearScreen();
        StartCoroutine(PrintBootScreen());
    }

    // For computers only (ie printing error codes and ready messages), interpreters print statement handles control codes etc
    // TODO add time penalty, based off of string length
    public void PrintFormattedTextToScreen(string message)
    {
        string newMessage = "";

        for (int i = 0; i < message.Length; i++) newMessage += (char)(message[i] + 224); // convert normal character to screen printable ones, expects only lowercase as uppercase are graphics characters

        int index;
        byte logicalLineValue = 2;

        string[] lines = SeperateStringByLine(newMessage);

        for (int l = 0; l < lines.Length; l++)
        {
            index = (YPos * _vm.ScreenWidth) + XPos;

            // prints string to text field
            TextField.text = TextField.text.Remove(index, lines[l].Length).Insert(index, lines[l]);

            // place message bytes into ram, the cursor position + screen space start is indexed by the characters in the string to write the data
            for (int i = 0; i < lines[l].Length; i++)
            {
                _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index + i, (byte)Array.IndexOf(_vm.CharacterSetValues, lines[l][i]));
            }

            if (lines.Length > 1)
            {
                // first line is 2, second is 3, when it becomes 4 the if block is false and 1s are written from then on
                if (logicalLineValue < 4)
                {
                    _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, logicalLineValue);
                    logicalLineValue++;
                }
                else _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 1);
            }
            else
            {
                // line being overwritten is the top of a 2-long logical line
                if (_vm.Ram[_vm.SimpleLogicalLines + YPos] == 2) _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos + 1, 1);

                _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 1);
            }

            CarriageReturn(); // shift down to next line, will scroll screen if necessary
        }

        FlashCursor();
    }

    // prints the characters submitted directly to the screen, they are not shifted
    public void PrintRawTextToScreen(string message)
    {
        int index;
        byte logicalLineValue = 2;

        string[] lines = SeperateStringByLine(message);

        for (int l = 0; l < lines.Length; l++)
        {
            index = (YPos * _vm.ScreenWidth) + XPos;

            // prints string to text field
            TextField.text = TextField.text.Remove(index, lines[l].Length).Insert(index, lines[l]);

            // place message bytes into ram, the cursor position + screen space start is indexed by the characters in the string to write the data
            for (int i = 0; i < lines[l].Length; i++)
            {
                _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index + i, (byte)Array.IndexOf(_vm.CharacterSetValues, lines[l][i]));
            }

            if (lines.Length > 1)
            {
                // first line is 2, second is 3, when it becomes 4 the if block is false and 1s are written from then on
                if (logicalLineValue < 4)
                {
                    _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, logicalLineValue);
                    logicalLineValue++;
                }
                else _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 1);
            }
            else
            {
                // line being overwritten is the top of a 2-long logical line
                if (_vm.Ram[_vm.SimpleLogicalLines + YPos] == 2) _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos + 1, 1);

                _vm.WriteByteToRam(_vm.SimpleLogicalLines + YPos, 1);
            }

            CarriageReturn(); // shift down to next line, will scroll screen if necessary
        }


        // for (int i = 0; i < message.Length; i++)
        // {
        //     int index = (_vm.Ram[_vm.CursorPositionY] * _vm.ScreenWidth) + _vm.Ram[_vm.CursorPositionX];

        //     TextField.text = TextField.text.Remove(index, 1).Insert(index, message[i].ToString()); // insert character to textbox
        //     _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index, (byte)Array.IndexOf(_vm.CharacterSetValues, message[i])); // place byte into ram
        //     CursorRight(false);
        // }

        // CarriageReturn();

        FlashCursor();
    }

    void ScrollScreen()
    {
        // wipe the first line of characters in the text box and add a blank row
        TextField.text = TextField.text.Remove(0, _vm.ScreenWidth) + (Space * 40); //! TODO ensure this multiplication works
        int screenSpaceStart = _vm.ReadWordFromRam(_vm.pScreenSpaceStart);

        // shift screen bytes upwards
        Array.Copy(_vm.Ram, screenSpaceStart + _vm.ScreenWidth, _vm.Ram, screenSpaceStart, _vm.ScreenWidth * (_vm.ScreenHeight - 1));

        // clear bottom screen row
        for (int x = 0; x < _vm.ScreenWidth; x++) _vm.Ram[screenSpaceStart + (_vm.ScreenWidth * (_vm.ScreenHeight - 1)) + x] = 0;

        _interpreter.TotalBudget -= 20;

        ScrollSimpleLogicalLines();
        RefreshScreen();
    }

    void ScrollSimpleLogicalLines()
    {
        // shift lines down by 1
        Array.Copy(_vm.Ram, _vm.SimpleLogicalLines + 1, _vm.Ram, _vm.SimpleLogicalLines, _vm.ScreenHeight - 1);
        _vm.WriteByteToRam(_vm.SimpleLogicalLines + _vm.ScreenHeight - 1, 0); // clear last line

        // only skip double when in screen editor, not program running (eg listing)
        if (_vm.Ram[_vm.SimpleLogicalLines] == 3 && !_interpreter.IsProgramRunning) ScrollScreen(); // scroll the screen an extra line to avoid cutting a logical line in half

        if (_vm.Ram[_vm.SimpleLogicalLines] == 3) _vm.WriteByteToRam(_vm.SimpleLogicalLines, 1); // if top line of screen is a 2-long cut in half, make it 1
        RefreshScreen();
    }

    public void FlashCursor()
    {
        if (CanFlashCursor) _cursorPosition.UpdateCursorPosition();
    }

    bool IsLineEmpty(int row) => TextField.text.Substring(_vm.ScreenWidth * row, _vm.ScreenWidth).Replace(Space.ToString(), "").Replace(ReversedSpace.ToString(), "") == "";

    bool IsCharacterEmpty(int index) => TextField.text[index] == Space || TextField.text[index] == ReversedSpace;

    // rebuilds screen UI from bytes in ram
    public void RefreshScreen()
    {
        int amount = _vm.ScreenHeight * _vm.ScreenWidth;
        string newScreen = "";
        for (int i = 0; i < amount; i++)
        {
            newScreen += _vm.CharacterSetValues[_vm.Ram[_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + i]];
        }
        TextField.text = newScreen;
        print("screen refreshed");
    }

    public void CarriageReturn()
    {
        CursorDown(false);
        XPos = 0;
    }

    public void CarriageReturn(int count)
    {
        for (int i = 0; i < count; i++) CursorDown(false);
        XPos = 0;
    }

    public string[] SeperateStringByLine(string inputString)
    {
        List<string> strings = new List<string>();

        for (int i = 0; i < inputString.Length; i += 40)
        {
            strings.Add(inputString.Substring(i, Math.Min(40, inputString.Length - i)));
        }

        return strings.ToArray();
    }

    // TODO remove this
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.F9)) RefreshScreen();
        if (Input.GetKeyDown(KeyCode.F10)) DebugPrintProgram();
    }

    private void DebugPrintProgram()
    {
        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart);

        string output = "";

        while (address != _vm.ReadWordFromRam(_vm.pProgramSpaceEnd))
        {
            print(address);
            output += _vm.Ram[address] + " ";
            address++;
        }

        output += $"\n\nPrg Start: {_vm.ReadWordFromRam(_vm.pProgramSpaceStart)}\nPrg End: {_vm.ReadWordFromRam(_vm.pProgramSpaceEnd)}\nVar end:{_vm.ReadWordFromRam(_vm.pVariableSpaceEnd)}\nArr end:{_vm.ReadWordFromRam(_vm.pArraySpaceEnd)}";

        print(output);
    }


    // TODO remove pauses after prints and make it base kit. Delays are relative to frame rate

    // screen flash that occurs when the machine is powered on
    IEnumerator Startup()
    {
        CanScrollScreen = false;
        SetCanFlashCursor(false);

        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");
        PrintFormattedTextToScreen("Ð@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@ÐÐ@@Ð");

        CursorHome();
        yield return new WaitForSeconds(0.25f);
        for (int i = 0; i < 5; i++)
        {
            PrintFormattedTextToScreen("                                        ");
            PrintFormattedTextToScreen("                                        ");
            PrintFormattedTextToScreen("                                        ");
            PrintFormattedTextToScreen("                                        ");
            PrintFormattedTextToScreen("                                        ");
            yield return new WaitForSeconds(0.01f);
        }

        CursorHome();
        yield return new WaitForSeconds(0.05f);

        yield return PrintBootScreen();

        CanScrollScreen = true;
        SetCanFlashCursor(true);
    }

    // display message boot screen
    IEnumerator PrintBootScreen()
    {
        SetCanFlashCursor(false);

        CursorHome();
        yield return new WaitForSeconds(0.05f);
        // PrintFormattedTextToScreen("", true);
        CarriageReturn();

        PrintFormattedTextToScreen(" ****  lmc-77  ****");
        yield return new WaitForSeconds(0.05f);
        // PrintFormattedTextToScreen("", true);
        CarriageReturn();

        PrintFormattedTextToScreen(" 5k ram system  3584 simple bytes free");
        CarriageReturn();

        PrintFormattedTextToScreen(" (c) 2026 liam latz");

        yield return new WaitForSeconds(0.05f);
        // PrintFormattedTextToScreen("", true);
        CarriageReturn();

        // PrintFormattedTextToScreen("", true);
        CarriageReturn();

        PrintFormattedTextToScreen("ready.");

        SetCanFlashCursor(true);

    }


}
