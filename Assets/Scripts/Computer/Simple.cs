using System.Collections.Generic;
using UnityEngine;
using Types;
using System;

public class Simple : MonoBehaviour
{
    [SerializeField] VirtualMachine _vm;
    [SerializeField] Tokeniser _tokeniser;
    [SerializeField] KeyboardAndScreenEditor _keyboard;
    [SerializeField] Interpreter _interpreter;

    // TODO update memory map in other scripts
    //* Simple program memory map 

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

    //* Simple line
    // [0..1]     word pointer to next line         
    // [2..3]     word line number                           
    // [4..^2]    line bytes                        
    // [^1]       255 terminator   

    public void AddLine(word lineNumber, byte[] lineBytes)
    {
        // isnt enough space in the program for the new line
        CheckForSizeLeft(lineBytes.Length + 5, "new line");


        // line already exists and should be replaced
        if (GetAllLines().TryGetValue(lineNumber, out _)) DeleteLine(lineNumber);

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart);

        while (_vm.ReadWordFromRam(address) != 65535) // double 255 bytes is the end of program terminator
        {
            if (_vm.ReadWordFromRam(address + 2) > lineNumber) break; // line number is greater, the new line should be placed in this place
            address = _vm.ReadWordFromRam(address); // move up to next line
        }

        print("Storing line at address " + address);

        UpdateLinePointersFromAddress(address, lineBytes.Length + 5); // shift pointers to match new address they will be moved to
        print($"{address}..{_vm.ReadWordFromRam(_vm.pArraySpaceEnd) - (lineBytes.Length + 5)}");

        byte[] upperProgram = _vm.Ram[address..(_vm.ReadWordFromRam(_vm.pArraySpaceEnd) - (lineBytes.Length + 5))]; // cache upper program (with their new pointers) - length of line since array space pointer was increased

        WriteByteLineToRam(address, lineNumber, lineBytes); // write line, overwriting program already there

        Array.Copy(upperProgram, 0, _vm.Ram, address + 5 + lineBytes.Length, upperProgram.Length); // move upper program after new line, pointers are now correct
        _keyboard.RefreshScreen(); // since the array.copy is editing bytes in ram that could possibly be on the screen
    }

    public void DeleteLine(int lineNumber)
    {
        Dictionary<word, word> lines = GetAllLines();
        if (!lines.TryGetValue(lineNumber, out _))
        {
            print("Line number does not exist"); // TODO remove this
            return; // line number does not exist
        }

        word nextLineAddress = _vm.ReadWordFromRam(lines[lineNumber]); // follow pointer to next line
        word currentLineAddress = lines[lineNumber];

        int length = nextLineAddress - currentLineAddress;
        UpdateLinePointersFromAddress(nextLineAddress, -length); // these lines will be shifted down so adjust the ram pointers

        byte[] upperProgram = _vm.Ram[nextLineAddress..(_vm.ReadWordFromRam(_vm.pArraySpaceEnd) + length)]; // up until the end of the program as well as the variables and arrays, + length since arraySpaceEnd has already been shifted down

        Array.Copy(upperProgram, 0, _vm.Ram, currentLineAddress, upperProgram.Length); // shifting the upper program down to the deleted line, overwriting its contents
        _keyboard.RefreshScreen(); // since the array.copy is editing bytes in ram that could possibly be on the screen
    }

    // creates byte line and places it into ram from the specified address
    void WriteByteLineToRam(word startAddress, word lineNumber, byte[] lineBytes)
    {
        word linePointer = startAddress + 5 + lineBytes.Length;

        byte[] array = new byte[5 + lineBytes.Length]; // +5 = 2 words plus terminator

        array[0] = linePointer.Lo;
        array[1] = linePointer.Hi;
        array[2] = lineNumber.Lo;
        array[3] = lineNumber.Hi;
        for (int i = 0; i < lineBytes.Length; i++) array[i + 4] = lineBytes[i]; // write line bytes into array
        array[^1] = 255; // last byte of array is terminator

        for (int x = 0; x < array.Length; x++) _vm.WriteByteToRam(startAddress + x, array[x]); // place bytes into ram
    }

    public Dictionary<word, word> GetAllLines()
    {
        int address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart);

        Dictionary<word, word> lineNumbers = new Dictionary<word, word>();

        while (_vm.ReadWordFromRam(address) != 65535) // double 255 bytes is the end of program terminator
        {
            if (address > _vm.Ram.Length)
            {
                Debug.LogError("ERROR: No program terminator found"); // TODO make proper error message and put this where program looping is used to make saft
                return lineNumbers;
            }
            lineNumbers.Add(_vm.ReadWordFromRam(address + 2), address);
            address = _vm.ReadWordFromRam(address);
        }

        return lineNumbers;
    }

    // adds given value to all pointers in line after the provided address. address must be the pointer at the start of a simple line
    public void UpdateLinePointersFromAddress(word address, word changeValue)
    {
        word nextLine;
        word currentLine = address;

        while (_vm.ReadWordFromRam(currentLine) != 65535)
        {
            nextLine = _vm.ReadWordFromRam(currentLine);

            _vm.WriteWordToRam(currentLine, _vm.ReadWordFromRam(currentLine) + changeValue); // shift pointer

            currentLine = nextLine;
        }

        // shifting end of program space pointers
        _vm.WriteWordToRam(_vm.pProgramSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceEnd) + changeValue);
        _vm.WriteWordToRam(_vm.pVariableSpaceEnd, _vm.ReadWordFromRam(_vm.pVariableSpaceEnd) + changeValue);
        _vm.WriteWordToRam(_vm.pBoolSpaceEnd, _vm.ReadWordFromRam(_vm.pBoolSpaceEnd) + changeValue);
        _vm.WriteWordToRam(_vm.pArraySpaceEnd, _vm.ReadWordFromRam(_vm.pArraySpaceEnd) + changeValue);


        _vm.CalculateFreeBytes(); // the amount of program memory available has changed
    }

    // NEW
    public void NewProgram()
    {
        _interpreter.SkipToken();

        _vm.WriteWordToRam(_vm.ReadWordFromRam(_vm.pProgramSpaceStart), 65535); // double 255 terminator indicated end of program

        _vm.WriteWordToRam(_vm.pProgramSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceStart) + 2); // program ends after terminators

        DeleteAllVariables(); // clears variables (and program) and set pointers back to start of program

        _vm.CalculateFreeBytes();
    }

    // ---------------------------------------------- //

    //? this also counts as garbage collection. Any unused variables will be trashed and not recreated in the next program.
    // wipes the variables from ram and resets pointers
    public void DeleteAllVariables()
    {
        //* lower variables

        //? If the program was cleared (end of program pointer set to start + 2), then this encompases the old program also.
        int lowerVariableLength = _vm.ReadWordFromRam(_vm.pArraySpaceEnd) - _vm.ReadWordFromRam(_vm.pProgramSpaceEnd); // variables are from end of program to end of arrays, encompases bools and numbers as well. 

        print(_vm.ReadWordFromRam(_vm.pArraySpaceEnd) + ", " + _vm.ReadWordFromRam(_vm.pProgramSpaceEnd));
        print(lowerVariableLength);


        _vm.WriteByteArrayToRam(_vm.ReadWordFromRam(_vm.pProgramSpaceEnd), new byte[lowerVariableLength]); // an array of 0s the size of the variable space will override the data

        //* upper variables
        int upperVariableLength = _vm.MemorySize - _vm.ReadWordFromRam(_vm.pStringSpaceEnd); // from string space to top of ram

        _vm.WriteByteArrayToRam(_vm.ReadWordFromRam(_vm.pStringSpaceEnd), new byte[upperVariableLength]); // write over upper variables with array of 0s

        // variables are cleared, pointers go back to end of program
        _vm.WriteWordToRam(_vm.pVariableSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceEnd));
        _vm.WriteWordToRam(_vm.pBoolSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceEnd));
        _vm.WriteWordToRam(_vm.pArraySpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceEnd));

        // move string and marker space back to top of ram
        _vm.WriteWordToRam(_vm.pStringSpaceEnd, _vm.MemorySize);
    }

    //* variable space

    //00 float
    // [0..1] variable name
    // [2..5] float data

    //01 string
    // [0..1] variable name (most significant bit of byte 1 set)
    // [2] string length
    // [3..4] strings address
    // [5] empty

    //11 int
    // [0..1] variable name (most significant bit of byte 0 and 1 set)
    // [2..3] signed integer value
    // [4..5] empty

    //* bool space

    // bool
    // [0..1] variable name (significant bits of 0 and 1 determine bool value)


    //* array space

    // [0..1] variable name
    // [2..3] size of array variable ((product of all dimensions * value length) + metadata )
    // [4] dimensionality
    // [5..6] length of first dimension
    // ... repeat for all dimensions
    // [n..^1] actual array of variables

    //* int array entry
    // [0..1] value

    //* float array entry
    // [0..3] value

    //* string array entry
    // [0] string length
    // [1..2] pointer to lovation in string space

    //* bool array entry
    // [0] = [][][][][][][][X] single bit for stored value


    // variable name arguments assumed to be in screen text

    // scans through simple lines and sees if a marker starts any of them, if it finds it, returns the line number
    public float GetMarkerVariable(string name) // TODO look for variable full string. throw error if it doesnt exist
    {
        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart);

        while (_vm.ReadWordFromRam(address) != 65535)
        {
            if (((char)(_vm.CharacterSetValues[_vm.Ram[address + 4]] - 224)).Equals('@')) // found marker
            {
                // TODO force a space after a marker in the expand string portion of the tokeniser?
                //! This comparison method requires a space after a marker, otherwise it will never find it

                byte[] byteMarker = _vm.Ram[(address + 5)..(address + 5 + name.Length + 1)]; // potential match taken from ram
                if (byteMarker[^1] == 255) byteMarker[^1] = 0; // if the byte after the marker is an end of line terminator, treat it as a space

                // add the space after the name so that shorter versions of names dont match. For example, looking for a marker called "FO" should not match with a marker called "FOO". With a space "FO " does not match "FOO"
                if (_vm.DoByteArraysMatch(_vm.ScreenTextToByteArray(name + KeyboardAndScreenEditor.Space), byteMarker)) // marker matches name. address + 5 is line after pointer(2) number(2) and @(1)
                    return (float)_vm.ReadWordFromRam(address + 2); // return line number of line
            }

            address = _vm.ReadWordFromRam(address); // follow pointer to next line
        }

        throw new InterpreterException("05 illegal quantity error", "specified marker does not exist");
    }

    // ----------------------- //

    // TODO combine reused code into methods

    public void CreateIntArray(string name, word[] dimensionLengths)
    {
        word variableName;

        // int so both significant bits set
        if (name.Length > 1) variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most
        else variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), 128); // only one character, highest bit of second char still set

        // TODO check if variable is already defined, throw error if it is
        // try catch a getarrayvariable?

        word sizeOffset = 1;
        foreach (word dimension in dimensionLengths) sizeOffset *= dimension; // get the product of all dimensions to determine the total size of the array.
        // add in the length of the variable metadata
        sizeOffset *= 2; // each entry is 2 bytes
        sizeOffset += 5 + (dimensionLengths.Length * 2); // metadata and length lists

        if (_vm.ReadWordFromRam(_vm.pArraySpaceEnd) + sizeOffset > _vm.ReadWordFromRam(_vm.pStringSpaceEnd)) throw new InterpreterException("04 out of memory error", "insufficient space for new array");

        byte[] resultArray = new byte[sizeOffset]; // create bytes for the new array. Include full length to use 0 to write over used ram spaces

        resultArray[0] = variableName.Lo;
        resultArray[1] = variableName.Hi;

        resultArray[2] = sizeOffset.Lo;
        resultArray[3] = sizeOffset.Hi;

        resultArray[4] = (byte)dimensionLengths.Length;

        // write all the dimension lengths in
        for (int i = 0, x = 0; i < dimensionLengths.Length; i++, x += 2)
        {
            word currentDimensionLength = dimensionLengths[i];

            resultArray[x + 5] = currentDimensionLength.Lo;
            resultArray[x + 6] = currentDimensionLength.Hi;
        }

        resultArray[^1] = 255; // TODO remove this its for testing purposes

        // remaining array is left 0 as it is the arrays values 

        _vm.WriteByteArrayToRam(_vm.ReadWordFromRam(_vm.pArraySpaceEnd), resultArray);

        _vm.WriteWordToRam(_vm.pArraySpaceEnd, _vm.ReadWordFromRam(_vm.pArraySpaceEnd) + sizeOffset); // increase array space by size of new array
    }

    public void CreateFloatArray(string name, word[] dimensionLengths)
    {

    }

    public void CreateStringArray(string name, word[] dimensionLengths)
    {

    }

    public void CreateBoolArray(string name, word[] dimensionLengths)
    {

    }

    // ------------- //

    public object GetIntArray(string name, word[] indexes)
    {
        word variableName;

        // in has both significant bits set
        if (name.Length > 1) variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most
        else variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), 128); // only one character, highest bit of second char still set

        word address = _vm.ReadWordFromRam(_vm.pBoolSpaceEnd); // bool space end is array space start

        while (address < _vm.ReadWordFromRam(_vm.pArraySpaceEnd))
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found array

            address += _vm.ReadWordFromRam(address + 2); // read length from array and add that to address to jump over current array
        }

        if (address == _vm.ReadWordFromRam(_vm.pArraySpaceEnd)) // specified array doesnt exist
        {
            throw new InterpreterException("not implemented exception", ""); //TODO remove

            //TODO create array if less than 10 in each dimension, else throw error
        }

        // ensure provided dimensionality match the dimensionality of the variable
        if (_vm.Ram[address + 4] != indexes.Length) throw new InterpreterException("05 illegal quantity error", "incorrect number of dimensions for array");

        // ensure indexes are within bounds for all dimensions. compare index with stored dimensions in variable
        for (int i = 0; i < indexes.Length; i++)
        {
            if (indexes[i] < 0 || indexes[i] > _vm.ReadWordFromRam(address + 5 + (i * 2))) throw new InterpreterException("05 illegal quantity error", "index outside the bounds of the array");
        }

        word totalOffset = 0;

        // create the lengths with a 1 at the beginning to use for proper indexing
        word[] dimensionLengths = new word[indexes.Length + 1];
        for (int d = 0; d < indexes.Length; d++) dimensionLengths[d + 1] = _vm.ReadWordFromRam(address + 5 + (d * 2)); // copy dimension max lengths into array
        dimensionLengths[0] = 1;

        // the stride of an index is the product of the max size of all indexes below it. so first index is (1), second is (first * 1), third is (second * first * 1)

        for (int i = 0; i < indexes.Length; i++) // go up the indexes
        {
            word stride = 1;

            // go down the dimension lengths starting from the index
            for (int x = i; x > 0; x--) stride *= dimensionLengths[x]; // get the product of all dimensions lengths below the current index

            print($"GET: stride of index {i} = {stride}");

            totalOffset += stride * indexes[i]; // stride is the number to skip per index
        }

        totalOffset *= 2; // an int is 2 bytes long

        print($"GET: total offset is {totalOffset}");

        short value = (short)_vm.ReadWordFromRam(address + 5 + (2 * indexes.Length) + totalOffset); // cast from word to short because short is signed

        return (float)value;
    }

    public object GetFloatArray(string name, word[] indexes)
    {
        return null;
    }

    public object GetStringArray(string name, word[] indexes)
    {
        return null;
    }

    public object GetBoolArray(string name, word[] indexes)
    {
        return null;
    }

    // -------- //

    public void SetIntArray(string name, word[] indexes, float value)
    {
        word variableName;

        // in has both significant bits set
        if (name.Length > 1) variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most
        else variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), 128); // only one character, highest bit of second char still set

        word address = _vm.ReadWordFromRam(_vm.pBoolSpaceEnd); // bool space end is array space start

        while (address < _vm.ReadWordFromRam(_vm.pArraySpaceEnd))
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found array

            address += _vm.ReadWordFromRam(address + 2); // read length from array and add that to address to jump over current array
        }

        if (address == _vm.ReadWordFromRam(_vm.pArraySpaceEnd)) // specified array doesnt exist
        {
            throw new InterpreterException("not implemented exception", ""); //TODO remove

            //TODO create array if less than 10 in each dimension, else throw error
        }

        // ensure provided dimensionality match the dimensionality of the variable
        if (_vm.Ram[address + 4] != indexes.Length) throw new InterpreterException("05 illegal quantity error", "incorrect number of dimensions for array");

        // ensure indexes are within bounds for all dimensions. compare index with stored dimensions in variable
        for (int i = 0; i < indexes.Length; i++)
        {
            if (indexes[i] < 0 || indexes[i] > _vm.ReadWordFromRam(address + 5 + (i * 2))) throw new InterpreterException("05 illegal quantity error", "index outside the bounds of the array");
        }

        word totalOffset = 0;

        // create the lengths with a 1 at the beginning to use for proper indexing
        word[] dimensionLengths = new word[indexes.Length + 1];
        for (int d = 0; d < indexes.Length; d++) dimensionLengths[d + 1] = _vm.ReadWordFromRam(address + 5 + (d * 2)); // copy dimension max lengths into array
        dimensionLengths[0] = 1;

        // the stride of an index is the product of the max size of the indexes below it. so first index is (1), second is (first * 1), third is (second * first * 1)

        for (int i = 0; i < indexes.Length; i++) // go up the indexes
        {
            word stride = 1;

            // go down the dimension lengths starting from the index
            for (int x = i; x > 0; x--) stride *= dimensionLengths[x]; // get the product of all dimensions lengths below the current index

            print($"GET: stride of index {i} = {stride}");

            totalOffset += stride * indexes[i]; // stride is the number to skip per index
        }

        totalOffset *= 2; // an int is 2 bytes long

        print($"GET: total offset is {totalOffset}");

        byte[] valueBytes = BitConverter.GetBytes((short)value); // convert value into bytes of its signed 16-bit 

        // write value into space
        _vm.WriteByteToRam(address + 5 + (2 * indexes.Length) + totalOffset, valueBytes[0]);
        _vm.WriteByteToRam(address + 5 + (2 * indexes.Length) + totalOffset + 1, valueBytes[1]);

    }

    public void SetFloatArray(string name, word[] indexes, float value)
    {

    }

    public void SetStringArray(string name, word[] indexes, string value)
    {

    }

    public void SetBoolArray(string name, word[] indexes, bool value)
    {

    }



    // ------------------- //

    public string GetStringVariable(string name)
    {
        word variableName;

        if (name.Length > 1) variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most, set bit on second character
        else variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), 128); // only one character, significant bit still set

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd); // program space end is var space start

        while (address < _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // search for variable
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found variable
            address += 6;
        }

        // couldnt find variable of name, create one. Either way variable is now at address
        if (address == _vm.ReadWordFromRam(_vm.pVariableSpaceEnd))
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateStringVariable(name, "");
            return ""; // create variable already sets value
        }

        byte length = _vm.Ram[address + 2];

        word stringAddress = _vm.ReadWordFromRam(address + 3) - length; // address is for the top end of the string, we want to grab it from the bottom

        print($"getting string from address {stringAddress}");

        string result = "";

        for (int i = 0; i < length; i++)
        {
            result += _vm.CharacterSetValues[_vm.Ram[stringAddress + i]]; // loop over the strings contents
        }

        return result;
    }

    public float GetIntVariable(string name)
    {
        // create word with byte values of string name, no significant bits so word is just the two bytes
        word variableName;

        if (name.Length > 1) variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most
        else variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), 128); // only one character, highest bit of second char still set

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd);

        while (address < _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // search for variable
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found variable
            address += 6;
        }

        if (address == _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // couldnt find variable of name
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateIntVariable(name, 0f);
            return 0f; // already know value, no need to calculate
        }

        // address now resides at correct variable
        return BitConverter.ToInt16(_vm.Ram[(address + 2)..(address + 4)]); // read 2 bits from variable, not a word since an int is singed
    }

    public float GetFloatVariable(string name)
    {
        // create word with byte values of string name, no significant bits so word is just the two bytes
        word variableName;

        if (name.Length > 1) variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), (byte)Array.IndexOf(_vm.CharacterSetValues, name[1])); // two characters at most
        else variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), 0); // only one character

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd);

        while (address < _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // search for variable
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found variable
            address += 6;
        }

        if (address == _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // couldnt find variable of name
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateFloatVariable(name, 0f);
            return 0f; // already know value, no need to calculate
        }

        // address now resides at correct variable
        return BitConverter.ToSingle(_vm.Ram[(address + 2)..(address + 6)]); // read 4 bits from variable
    }

    public bool GetBoolVariable(string name)
    {
        word address = _vm.ReadWordFromRam(_vm.pVariableSpaceEnd);

        word variableName;

        if (name.Length > 1) variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), (byte)Array.IndexOf(_vm.CharacterSetValues, name[1])); // two characters at most
        else variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), 0); // only one character

        while (address < _vm.ReadWordFromRam(_vm.pBoolSpaceEnd)) // search for variable
        {
            word boolName = _vm.ReadWordFromRam(address);
            // remove value from variable name so that they match
            boolName.Lo &= 0b01111111; // set significant bit to 0
            boolName.Hi &= 0b01111111; // set significant bit to 0

            if (boolName == variableName) break; // found variable
            address += 2;
        }

        if (address == _vm.ReadWordFromRam(_vm.pBoolSpaceEnd))
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateBoolVariable(name, false); // couldnt find variable of name
            return false; // already know value, no need to calculate
        }

        return _vm.ReadBitFromRam(address, 7) && _vm.ReadBitFromRam(address + 1, 7); // return true if both significant bits of variable name equal 1, else return false. (so if 1 bit was set and one was not somehow, it would return false)
    }

    // ------

    public void SetIntVariable(string name, float value)
    {
        // if provided number can safely be converted to 16 bit signed integer (float is floored for this calculation)
        if ((int)value > 32767 || (int)value < -32766) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

        word variableName;

        // create word with byte values of string name, both significant bits set
        if (name.Length > 1) variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most
        else variableName = new word((byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128), 128); // only one character, significant bit of char 2 is still set

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd); // program space end is variable space start

        while (address < _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // search for variable
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found variable
            address += 6;
        }

        // couldnt find variable of name, create one. Either way variable is now at address
        if (address == _vm.ReadWordFromRam(_vm.pVariableSpaceEnd))
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateIntVariable(name, value);
            return; // create variable already sets value
        }

        byte[] intBytes = BitConverter.GetBytes((short)value);

        _vm.WriteByteArrayToRam(address + 2, intBytes); // copy float value into ram, +2 to jump over variable name
    }

    public void SetFloatVariable(string name, float value)
    {
        word variableName;

        // create word with byte values of string name, no significant bits so word is just the two bytes
        if (name.Length > 1) variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), (byte)Array.IndexOf(_vm.CharacterSetValues, name[1])); // two characters at most
        else variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), 0); // only one character

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd); // program space end is variable space start

        while (address < _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // search for variable
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found variable
            address += 6;
        }

        // couldnt find variable of name, create one. Either way variable is now at address
        if (address == _vm.ReadWordFromRam(_vm.pVariableSpaceEnd))
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateFloatVariable(name, value);
            return; // create variable already sets value
        }

        byte[] floatBytes = BitConverter.GetBytes(value);

        _vm.WriteByteArrayToRam(address + 2, floatBytes); // copy float value into ram, +2 to jump over variable name
    }

    public void SetBoolVariable(string name, bool value)
    {
        word address = _vm.ReadWordFromRam(_vm.pVariableSpaceEnd); // variable space end is bool space start

        word variableName;

        // no significant bits set
        if (name.Length > 1) variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), (byte)Array.IndexOf(_vm.CharacterSetValues, name[1])); // two characters at most
        else variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), 0); // only one character

        while (address < _vm.ReadWordFromRam(_vm.pBoolSpaceEnd)) // search for variable
        {
            word boolName = _vm.ReadWordFromRam(address);
            // remove value from variable name so that they match
            boolName.Lo &= 0b01111111; // set significant bit to 0
            boolName.Hi &= 0b01111111; // set significant bit to 0

            if (boolName == variableName) break; // found variable
            address += 2;
        }

        if (address == _vm.ReadWordFromRam(_vm.pBoolSpaceEnd)) // variable not found, create one
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateBoolVariable(name, value);
            return; // create variable already sets value
        }

        if (value) // set both significant bits to 1
        {
            _vm.WriteBitToRam(address, 7, true);
            _vm.WriteBitToRam(address + 1, 7, true);
        }
        else // set both significant bits to 0
        {
            _vm.WriteBitToRam(address, 7, false);
            _vm.WriteBitToRam(address + 1, 7, false);
        }
    }

    public void SetStringVariable(string name, string value)
    {
        word variableName;

        if (name.Length > 1) variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128)); // two characters at most, set bit on second character
        else variableName = new word((byte)Array.IndexOf(_vm.CharacterSetValues, name[0]), 128); // only one character, significant bit still set

        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd); // program space end is var space start

        while (address < _vm.ReadWordFromRam(_vm.pVariableSpaceEnd)) // search for variable
        {
            if (_vm.ReadWordFromRam(address) == variableName) break; // found variable
            address += 6;
        }

        // couldnt find variable of name, create one. Either way variable is now at address
        if (address == _vm.ReadWordFromRam(_vm.pVariableSpaceEnd))
        {
            // throw new InterpreterException("08 undefined variable error", "variable must be defined with a let statement");
            CreateStringVariable(name, value);
            return; // create variable already sets value
        }

        word stringAddress = _vm.ReadWordFromRam(address + 3); // address of string literal

        byte length = _vm.Ram[address + 2]; // length of old variable
        _vm.WriteByteToRam(address + 2, (byte)value.Length); // set new length in variable

        int difference = length - value.Length; // variable is being overwritten so may share some of the string space, but may need more or less so program could shift either way

        print($"difference is {difference}");
        print($"{_vm.ReadWordFromRam(_vm.pStringSpaceEnd)}..{stringAddress - length}");

        byte[] upperStringSpace = _vm.Ram[_vm.ReadWordFromRam(_vm.pStringSpaceEnd)..(stringAddress - length)]; // from end of marker space to end of the strings value

        _vm.WriteByteArrayToRam(_vm.ReadWordFromRam(_vm.pStringSpaceEnd) + difference, upperStringSpace); // shift string and marker variables either upwards or downwards the difference between the new and old values length

        byte[] stringValue = new byte[value.Length];
        for (int i = 0; i < value.Length; i++) stringValue[i] = (byte)Array.IndexOf(_vm.CharacterSetValues, value[i]);
        _vm.WriteByteArrayToRam(stringAddress - value.Length, stringValue); // write string value into new space

        _vm.WriteWordToRam(_vm.pStringSpaceEnd, _vm.ReadWordFromRam(_vm.pStringSpaceEnd) + difference); // shift string space pointers by the difference

        //TODO other string variables locations in their vars should be adjusted with the difference
        // loop through strings, if their address is lower than (stringaddress - length) adjust for difference

        word stringType = new word(0, 128); //just the significant bits
        word searchAddress = _vm.ReadWordFromRam(_vm.pProgramSpaceEnd); // variable space start

        while (searchAddress != _vm.ReadWordFromRam(_vm.pVariableSpaceEnd))
        {
            word currentVar = new word((byte)(_vm.Ram[searchAddress] & 0b10000000), (byte)(_vm.Ram[searchAddress + 1] & 0b10000000)); // get significant bits of variable, dont care about the name

            // variable is a string
            if (currentVar == stringType && _vm.ReadWordFromRam(searchAddress + 3) <= stringAddress - length && _vm.ReadWordFromRam(searchAddress) != variableName) // make sure not to edit its own variable
            {
                word varAddress = _vm.ReadWordFromRam(searchAddress + 3);

                // string is above the one edited and thus its address wont be shifted with the strings
                _vm.WriteWordToRam(searchAddress + 3, varAddress + difference); // move address pointer of string by difference, since its location has been shifted

            }

            searchAddress += 6;
        }
    }





    // ------

    public void CreateStringVariable(string name, string value) // both params are in screen text
    {
        byte[] varResult = new byte[6];

        // variable name, significant bit of second byte set
        varResult[0] = (byte)Array.IndexOf(_vm.CharacterSetValues, name[0]);
        varResult[1] = name.Length > 1 ? (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128) : (byte)128; // if there is a second character, use it, else 0 with significant bit set

        if (value.Length > 255) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

        varResult[2] = (byte)value.Length; // insert length of string

        word address = _vm.ReadWordFromRam(_vm.pStringSpaceEnd); // place new string at the end

        // insert address into variable, byte 5 is left blank
        varResult[3] = address.Lo;
        varResult[4] = address.Hi;

        CheckForSizeLeft(6, "new variable");

        InsertVariableIntoRam(varResult);

        //* insert string value

        byte[] stringResult = new byte[value.Length];

        for (int i = 0; i < value.Length; i++) stringResult[i] = (byte)Array.IndexOf(_vm.CharacterSetValues, value[i]);

        if ((_vm.ReadWordFromRam(_vm.pStringSpaceEnd) - stringResult.Length) < _vm.ReadWordFromRam(_vm.pArraySpaceEnd)) throw new InterpreterException("04 out of memory error", "insufficent space for new variable");

        print(value);

        print($"writing {stringResult} to address {_vm.ReadWordFromRam(_vm.pStringSpaceEnd) - stringResult.Length}");
        _vm.WriteByteArrayToRam(_vm.ReadWordFromRam(_vm.pStringSpaceEnd) - stringResult.Length, stringResult); // insert string value into new gap created, just after current string space end

        _vm.WriteWordToRam(_vm.pStringSpaceEnd, _vm.ReadWordFromRam(_vm.pStringSpaceEnd) - stringResult.Length); // shift string space pointer down 6 bytes
    }

    public void CreateFloatVariable(string name, float value)
    {
        byte[] result = new byte[6]; // all variables are 6 bytes long

        byte[] floatByteValue = BitConverter.GetBytes(value);

        // variable name, no significant bits set
        result[0] = (byte)Array.IndexOf(_vm.CharacterSetValues, name[0]);
        result[1] = name.Length > 1 ? (byte)Array.IndexOf(_vm.CharacterSetValues, name[1]) : (byte)0; // if there is a second character, use it, else 0

        Array.Copy(floatByteValue, 0, result, 2, 4); // copy float value into result

        InsertVariableIntoRam(result); // find space and insert
    }

    public void CreateIntVariable(string name, float value)
    {
        // if provided number can safely be converted to 16 bit signed integer (float is floored for this calculation)
        if ((int)value > 32767 || (int)value < -32766) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

        byte[] result = new byte[6]; // all variables are 6 bytes long

        byte[] intByteValue = BitConverter.GetBytes((short)value);

        // set significant bit of both bytes to indicate int type
        result[0] = (byte)(Array.IndexOf(_vm.CharacterSetValues, name[0]) | 128);
        result[1] = name.Length > 1 ? (byte)(Array.IndexOf(_vm.CharacterSetValues, name[1]) | 128) : (byte)128; // if there is a second character, use it, else 0 with most significant bit set

        Array.Copy(intByteValue, 0, result, 2, 2); // copy int value into result (2 bytes, remaining 2 are blank)

        InsertVariableIntoRam(result); // find space and insert
    }

    public void CreateBoolVariable(string name, bool value)
    {
        byte[] result = new byte[2];

        result[0] = (byte)Array.IndexOf(_vm.CharacterSetValues, name[0]);
        result[1] = name.Length > 1 ? (byte)Array.IndexOf(_vm.CharacterSetValues, name[1]) : (byte)0; // if there is a second character, use it

        if (value) // set both most significant bits to true
        {
            result[0] |= 128;
            result[1] |= 128;
        }
        else // set both most significant bits to false
        {
            result[0] &= 0b01111111;
            result[1] &= 0b01111111;
        }

        word address = _vm.ReadWordFromRam(_vm.pBoolSpaceEnd); // insert bool at end of bool space

        CheckForSizeLeft(2, "new variable");

        // shift upper program forward 2 bytes
        Array.Copy(_vm.Ram, address, _vm.Ram, address + 2, _vm.ReadWordFromRam(_vm.pArraySpaceEnd) - address);

        // place variable into new space
        _vm.WriteByteArrayToRam(address, result);

        // add 2 to the address pointers
        _vm.WriteWordToRam(_vm.pBoolSpaceEnd, _vm.ReadWordFromRam(_vm.pBoolSpaceEnd) + 2);
        _vm.WriteWordToRam(_vm.pArraySpaceEnd, _vm.ReadWordFromRam(_vm.pArraySpaceEnd) + 2);
    }

    // place variable at end of variable space and shift forward array space
    public void InsertVariableIntoRam(byte[] bytes)
    {
        word address = _vm.ReadWordFromRam(_vm.pVariableSpaceEnd); // insert at end of variable space

        CheckForSizeLeft(6, "new variable");

        print(_vm.ReadWordFromRam(_vm.pArraySpaceEnd) + ", " + address);

        // shift array space forward 6 bytes
        Array.Copy(_vm.Ram, address, _vm.Ram, address + 6, _vm.ReadWordFromRam(_vm.pArraySpaceEnd) - address);

        // place variable into new space
        _vm.WriteByteArrayToRam(address, bytes);

        string e = "";
        foreach (byte b in bytes) e += b + ", ";
        print($"storing {e} at address {address}");

        _vm.WriteWordToRam(_vm.pVariableSpaceEnd, _vm.ReadWordFromRam(_vm.pVariableSpaceEnd) + 6); // add 6 to variable space address
        // bool and array space is above variable space and is shifted up too
        _vm.WriteWordToRam(_vm.pBoolSpaceEnd, _vm.ReadWordFromRam(_vm.pBoolSpaceEnd) + 6);
        _vm.WriteWordToRam(_vm.pArraySpaceEnd, _vm.ReadWordFromRam(_vm.pArraySpaceEnd) + 6);

        _keyboard.RefreshScreen();
    }

    // ----------------- //

    // used when adding something to program space to see if there is enough memory to do so. 
    // Throws an exception if not enough space, does nothing if there is
    void CheckForSizeLeft(int size, string message)
    {
        if (_vm.ReadWordFromRam(_vm.pArraySpaceEnd) + size >= _vm.ReadWordFromRam(_vm.pStringSpaceEnd)) throw new InterpreterException("04 out of memory error", $"insufficient space for {message}");
    }

    // ------------------------------------  //? old code below (probably wont be used)

    // debug but also an example of how to translate the bytes into the actual code
    public void DebugCrawlProgram()
    {
        bool inQuotes = false;
        int address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart); // starting address of program space

        string total = "FROM BYTES\n\n";

        while (_vm.ReadWordFromRam(address) != 65535) // if line pointer is not 255 terminator
        {
            string line = "";

            line += $"next line: {_vm.ReadWordFromRam(address)}\n";
            line += $"Line number: {_vm.ReadWordFromRam(address + 2)}\nText: ";

            int index = 4;
            while (_vm.Ram[address + index] != 255)
            {
                byte b = _vm.Ram[address + index];

                if (b == 2)
                {
                    print("in quotes");
                    inQuotes = !inQuotes;
                }

                if (b < 128) line += (char)(_vm.CharacterSetValues[b] - 224); // text, shift down to readable
                else if (!inQuotes) // do not tokenise control codes within text
                {
                    print("printing token");
                    line += _tokeniser.KeywordStrings[b - 128];
                }

                index++;
            }

            address = _vm.ReadWordFromRam(address);

            total += line + "\n\n";
        }

        print(total);
    }
}
