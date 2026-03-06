using UnityEngine;
using Types;
using System.Collections.Generic;
using System;

public class Tokeniser : MonoBehaviour
{
    // Converts screen text from the textfield into tokenised bytes to be placed into ram
    // depending on the inputted line, it may delete a line from ram, store it, or execute it immediately

    // ---------------------------------------------- //

    [SerializeField] VirtualMachine _vm;
    [SerializeField] Simple _simple;
    [SerializeField] Interpreter _interpreter;
    [SerializeField] KeyboardAndScreenEditor _keyboard;

    readonly char[] _unformattedWhitespace = new char[] { ' ', (char)(' ' + 128) };

    int _stringIndex;

    SimpleLine _currentLine;

    public struct SimpleLine
    {
        public string StringValue; // line string without line number, unformmatted / raw screen text
        public string LineNumber;
    }

    //* line strings
    string _inputString; // raw screen text
    string _formattedString; // reversed and unreversed shifted down to ascii
    string _unFormattedString; // unreversed shifted down to ascii, reversed not raw but still illegible
                               // used with UnReversedCharacter(char) to see if a reversed character is its ascii equivalent

    public void TokeniseInputString(string inputString)
    {
        ExpandString(inputString);

        // determine what to do with inputted command

        if (_currentLine.LineNumber == "") // execute line immediatly
        {
            print("Executing " + ScreenTextToFormattedString(_currentLine.StringValue)); // TODO remove this

            // screen text --> bytes
            // bytes --> tokens
            // tokens passed into executeline

            foreach (Token token in TokeniseLineBytes(ByteTokeniseScreenText(_currentLine.StringValue)))
            {
                print(token.TokenType + ", " + token.StringValue);
            }

            StartCoroutine(_interpreter.InterpretSingleLine(TokeniseLineBytes(ByteTokeniseScreenText(_currentLine.StringValue))));
            return;
        }
        else if (_currentLine.StringValue == "") // no content other than line number, delete line
        {
            print("deleting line " + _currentLine.LineNumber); // TODO remove this
            _simple.DeleteLine(int.Parse(_currentLine.LineNumber)); // turn string line number into int

            _keyboard.CarriageReturn();
            return;
        }

        // else add line to simple program

        if (int.Parse(_currentLine.LineNumber) > 63999) // line number too high (this number is arbitrary but real total number would be 65535)
        {
            _keyboard.CarriageReturn();
            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen("01 syntax error: invalid line number");

            // nothing to catch an interpreter exception, can just return from method
            return;
        }

        print("storing line " + _currentLine.LineNumber + " with contents of " + ScreenTextToFormattedString(_currentLine.StringValue)); // TODO remove this

        try
        {
            _simple.AddLine(int.Parse(_currentLine.LineNumber), ByteTokeniseScreenText(_currentLine.StringValue));
        }
        catch (InterpreterException simpleException) // only catching out of memory error
        {
            _keyboard.CarriageReturn();
            _keyboard.CarriageReturn();

            if ((simpleException.ErrorType.Length + simpleException.Message.Length + 2) > 40)
            {
                _keyboard.PrintFormattedTextToScreen($"{simpleException.ErrorType}:");
                _keyboard.PrintFormattedTextToScreen($"{simpleException.Message}");
            }
            else _keyboard.PrintFormattedTextToScreen($"{simpleException.ErrorType}: {simpleException.Message}");
        }
        catch (Exception realException) // actual errors in my code are handled differently
        {
            _keyboard.CarriageReturn();
            _keyboard.CarriageReturn();

            _keyboard.PrintFormattedTextToScreen($"{realException.Message}".ToLower());
            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen($"{realException.StackTrace[..100]}...".ToLower());
            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen("this is an actual error, please report.");
        }

        _keyboard.CarriageReturn();
    }

    // expand any keyword shorthands, as well decimals with missing sides (such as ".2")
    // creates a currentLine struct which contains the line number, and the line string
    void ExpandString(string inputString)
    {
        // delete leading and trailing white space (adding one space at the end so parsing works correctly)
        EditLineStrings(ScreenTextToUnformattedString(inputString).TrimStart(_unformattedWhitespace).TrimEnd(_unformattedWhitespace) + " ");

        _stringIndex = 0;

        _currentLine = new SimpleLine();

        if (char.IsNumber(_formattedString[0])) ParseLineNumber(); // get the line number, if there is one
        else _currentLine.LineNumber = "";

        // trim start (now after line number), but leave one space in case a number like ".2" wants to check behind the decimal point
        EditLineStrings(" " + ScreenTextToUnformattedString(_inputString).TrimStart(_unformattedWhitespace));

        while (_stringIndex < _inputString.Length) // loop through all the characters
        {
            char currentChar = _formattedString[_stringIndex];

            switch (currentChar)
            {
                case '\"':
                    _stringIndex++;
                    while (!_formattedString[_stringIndex].Equals('\"')) // skip over quotes, its contents should not be expanded
                    {
                        if (_stringIndex == _formattedString.Length - 1) break; // no closing quote found
                        _stringIndex++;
                    }
                    break;
                case '!':
                    EditLineStrings(_unFormattedString.Remove(_stringIndex, 1).Insert(_stringIndex, "print"));
                    _stringIndex += 4; // skip past the full length of the keyword
                    if (!_formattedString[_stringIndex + 1].Equals(' ')) EditLineStrings(_unFormattedString.Insert(_stringIndex + 1, " ")); // if no space after, insert space
                    break;
                case '#':
                    EditLineStrings(_unFormattedString.Remove(_stringIndex, 1).Insert(_stringIndex, "com"));
                    _stringIndex += 2; // skip past the full length of the keyword 
                    if (!_formattedString[_stringIndex + 1].Equals(' ')) EditLineStrings(_unFormattedString.Insert(_stringIndex + 1, " ")); // if no space after, insert space
                    break;
                case '.':
                    ParseNumber();
                    break;
                default: // not a single character shorthand, look for keywords
                    if (char.IsLetter(currentChar)) ParseIdentifier();
                    break;
            }

            _stringIndex++;
        }

        _currentLine.StringValue = _inputString[1..]; // save the final string value, ignoring first character which is a space
    }

    void ParseNumber()
    {
        if (!char.IsNumber(_formattedString[_stringIndex - 1])) // case: .01
        {
            EditLineStrings(_unFormattedString.Insert(_stringIndex, "0"));
            _stringIndex++;
        }

        if (!char.IsNumber(_formattedString[_stringIndex + 1])) // case: 10.
        {
            EditLineStrings(_unFormattedString.Insert(_stringIndex + 1, "0"));
            _stringIndex++;
        }

        // if the number is just a decimal point: ".", then both cases will trigger and it will be converted to "0.0"
    }

    void ParseIdentifier()
    {
        print(_formattedString[_stringIndex] + " is a letter");

        int startIndex = _stringIndex;
        string totalToken = "";
        string lowerToken = "";

        bool hasCaps = false;

        while (char.IsLetter(_formattedString[_stringIndex]))
        {
            char character = _formattedString[_stringIndex];
            if (char.IsUpper(character)) hasCaps = true;
            else lowerToken += character;

            totalToken += character;
            _stringIndex++;
        }

        if (hasCaps && _shorthands.TryGetValue(totalToken, out string keyword)) // caps make a shorthand
        {
            EditLineStrings(_unFormattedString.Remove(startIndex, totalToken.Length).Insert(startIndex, keyword));

            _stringIndex = startIndex + keyword.Length - 1; // move index past full keyword
        }
        else if (hasCaps && !_shorthands.TryGetValue(totalToken, out _)) // caps dont make a shorthand, remove them to make an identifier
        {
            EditLineStrings(_unFormattedString.Remove(startIndex, totalToken.Length).Insert(startIndex, lowerToken)); // remove the keyword and replace with only lowercase version

            _stringIndex = startIndex + lowerToken.Length - 1; // move index past the new keyword
        }

        // if there are no caps then keyword can remain as is

        print("finished parsing identifier with character " + _formattedString[_stringIndex] + "at index" + _stringIndex);
    }

    // line number cannot be a decimal value, if a point is found, it will just be assumed to be a second number
    // 12.3 = [line number: 12], [number 0.3]
    void ParseLineNumber()
    {
        string lineNumber = "";

        // delete leading 0s
        EditLineStrings(_unFormattedString.TrimStart(new char[] { '0', (char)('0' + 128) }));

        if (char.IsNumber(_formattedString[0])) // number still there (not all zeros)
        {
            while (char.IsNumber(_formattedString[_stringIndex])) // parse number
            {
                lineNumber += _formattedString[_stringIndex];
                _stringIndex++;
            }
        }
        else // line number now gone, it was only zeros
        {
            lineNumber = "0";
        }

        _currentLine.LineNumber = lineNumber;

        EditLineStrings(_unFormattedString.Remove(0, lineNumber.Length)); // delete the line number

        _stringIndex = 0;
    }

    // "formatted" meaning that reversed text is treated as its unreversed character (shifted down to normal text)
    // SIMPLE commands are parsed normally even if reversed, but reversed text inside quotes are parsed as control characters
    public string ScreenTextToFormattedString(string inputString)
    {
        string result = "";

        for (int i = 0; i < inputString.Length; i++)
        {
            char character = (char)(inputString[i] - 224);

            // if a reversed character, shift down to unreversed
            if (Array.IndexOf(_vm.CharacterSetValues, inputString[i]) > 127) character = (char)(character - 128 - 64);

            result += character;
        }

        return result;
    }

    // "unformatted" meaning that reversed text is kept as unlegible (its char value has not been shifted downward)
    public string ScreenTextToUnformattedString(string inputString)
    {
        string result = "";

        for (int i = 0; i < inputString.Length; i++) result += (char)(inputString[i] - 224);

        return result;
    }

    public string UnformattedStringToScreenText(string inputString)
    {
        string result = "";

        for (int i = 0; i < inputString.Length; i++) result += (char)(inputString[i] + 224);

        return result;
    }

    // used to check what character a reversed character represents, since even when formatted, they are not legible
    public char UnReversedCharacter(char character) => (char)(character - 128);

    public void EditLineStrings(string unformattedString)
    {
        _inputString = UnformattedStringToScreenText(unformattedString);
        _formattedString = ScreenTextToFormattedString(_inputString);
        _unFormattedString = ScreenTextToUnformattedString(_inputString); // should already be done but kept just to make sure
    }

    // ---------------------------------------------------------------- //

    //@ expanded screen text ----> tokenised bytes

    int _bstringIndex = 0;
    string _bformattedString;
    string _binputString;
    bool _bquotes = false;

    List<byte> _lineBytes;

    // commands are tokenised to 1 byte, numbers, text and identifiers are stored as the bytes of their ascii text
    // called when a simple line needs to be stored in memory
    public byte[] ByteTokeniseScreenText(string inputString)
    {
        // tokenise commands, leave numbers, text, identifiers and spaces as characters
        // 1st: word pointer to the next simple line in memory (pointer to the address of the low byte of the next lines pointer)
        // 2nd: line number as a word
        // 3rd: tokenise commands, ascii for numbers, text and identifiers //* <---- only this is performed in this method
        // 4th: 255 terminator

        _lineBytes = new();
        _bquotes = false;

        _bformattedString = ScreenTextToFormattedString(inputString);
        _binputString = inputString;

        _bstringIndex = 0;

        while (_bstringIndex < _bformattedString.Length - 1) // skip last byte as it's always a space
        {
            char character = _bformattedString[_bstringIndex];

            if (character.Equals('\"'))
            {
                AddToBytes(_binputString[_bstringIndex]);
                _bquotes = !_bquotes;
            }

            // found start of keyword
            else if (!_bquotes && character.Equals('@')) ParseByteMarker();
            else if (!_bquotes && char.IsLetter(character)) ParseByteKeyword();
            else AddToBytes(_binputString[_bstringIndex]); // else if in quotes or not a letter

            _bstringIndex++;
        }

        return _lineBytes.ToArray();
    }

    void AddToBytes(char character)
    {
        byte value = (byte)Array.IndexOf(_vm.CharacterSetValues, character);
        if (value > 127 && !_bquotes) value -= 128; // if reversed text, unreverse (except in text)
        _lineBytes.Add(value); // add the byte value of raw text
    }

    // add letters and not tokenise
    void ParseByteMarker()
    {
        AddToBytes(_binputString[_bstringIndex]);
        _bstringIndex++;

        while (char.IsLetter(_bformattedString[_bstringIndex]))
        {
            AddToBytes(_binputString[_bstringIndex]);
            _bstringIndex++;
        }

        _bstringIndex--; // prevent character after marker from being skipped by increment in loop
    }

    // parses text not encased in quotes, doesnt check for caps since they have been removed by the expandText method
    void ParseByteKeyword()
    {
        string keyword = "";

        while (char.IsLetter(_bformattedString[_bstringIndex]))
        {
            keyword += _bformattedString[_bstringIndex];
            _bstringIndex++;

            if (_bformattedString[_bstringIndex].Equals('$') || _bformattedString[_bstringIndex].Equals('%') || _bformattedString[_bstringIndex].Equals('?')) // checked here since it is not a letter and would cause a break in the while loop
            {
                keyword += _bformattedString[_bstringIndex];
                _bstringIndex++;
                break; // type identifier indicated the end of the identifier
            }
        }


        _bstringIndex--; // ++ in loop will cause a character to be skipped

        if (_keyWords.TryGetValue(keyword, out TokenType type))
        {
            _lineBytes.Add((byte)type); // if the text is a keyword, take the enums int value as a byte and insert to array
        }
        else
        {
            foreach (char character in UnformattedStringToScreenText(keyword))
            {
                _lineBytes.Add((byte)Array.IndexOf(_vm.CharacterSetValues, character)); // add bytes to array
            }
        }

    }

    // -------------------------------------------------------- //

    //@ Tokenised bytes from ram ----> tokens

    // TODO make method that reads byte line from ram and turns them into tokens

    List<Token> _tokenLine;
    int _tokenIndex;
    byte[] _byteLine;

    public Token[] TokeniseLineBytes(byte[] byteLine) // expects the bytes only from the actual line, not the pointer or line number or terminator
    {
        _byteLine = byteLine;
        _tokenLine = new List<Token>();
        _tokenIndex = 0;

        for (; _tokenIndex < byteLine.Length; _tokenIndex++) // while not at end of line terminator
        {
            byte b = byteLine[_tokenIndex];

            char formattedB = (char)(_vm.CharacterSetValues[b] - 224);

            if (b > 127)
            {
                _tokenLine.Add(new Token(KeywordStrings[b - 128], (TokenType)b)); // byte already represents token
                continue;
            }

            switch (formattedB)
            {
                case '\"':
                    ParseTextFromBytes();
                    break;
                case '@':
                    ParseMarkerFromBytes();
                    break;
                // these operators string value doesnt change which makes filling it out irrelevant
                case '<':
                    if (_tokenIndex != _byteLine.Length - 1) // if not at end of line
                    {
                        char peekCL = (char)(_vm.CharacterSetValues[_byteLine[_tokenIndex + 1]] - 224); // looks at the next character to see if it is a two character token
                        if (peekCL.Equals('>'))
                        {
                            _tokenIndex++;
                            _tokenLine.Add(new Token("", TokenType.NotEqualTo));
                        }
                        else if (peekCL.Equals('='))
                        {
                            _tokenIndex += 2;
                            _tokenLine.Add(new Token("", TokenType.LessEqual));
                        }
                        else _tokenLine.Add(new Token("", TokenType.Less));

                    }
                    else _tokenLine.Add(new Token("", TokenType.Less));

                    break;
                case '>':
                    if (_tokenIndex != _byteLine.Length - 1) // if not at end of line
                    {
                        if (((char)(_vm.CharacterSetValues[_byteLine[_tokenIndex + 1]] - 224)).Equals('='))
                        {
                            _tokenIndex++;
                            _tokenLine.Add(new Token("", TokenType.GreaterEqual));
                        }
                        else _tokenLine.Add(new Token("", TokenType.Greater));
                    }
                    else _tokenLine.Add(new Token("", TokenType.Greater));
                    break;
                case '=':
                    if (_tokenIndex != _byteLine.Length - 1) // if not at end of line
                    {
                        if (((char)(_vm.CharacterSetValues[_byteLine[_tokenIndex + 1]] - 224)).Equals('='))
                        {
                            _tokenIndex++;
                            _tokenLine.Add(new Token("", TokenType.EqualTo));
                        }
                        else _tokenLine.Add(new Token("", TokenType.Equals));
                    }
                    else _tokenLine.Add(new Token("", TokenType.Equals));

                    break;
                case '+':
                    _tokenLine.Add(new Token("", TokenType.Plus));
                    break;
                case '-':
                    _tokenLine.Add(new Token("", TokenType.Minus));
                    break;
                case '*':
                    _tokenLine.Add(new Token("", TokenType.Multiply));
                    break;
                case '/':
                    _tokenLine.Add(new Token("", TokenType.Divide));
                    break;
                case '(':
                    _tokenLine.Add(new Token("", TokenType.OpenBracket));
                    break;
                case ')':
                    _tokenLine.Add(new Token("", TokenType.CloseBracket));
                    break;
                case ',':
                    _tokenLine.Add(new Token("", TokenType.Comma));
                    break;
                case '^':
                    _tokenLine.Add(new Token("", TokenType.Power));
                    break;
                case ' ': // space, do nothing
                    break;
                case ':':
                    _tokenLine.Add(new Token("", TokenType.Colon));
                    break;
                case '[':
                    _tokenLine.Add(new Token("", TokenType.OpenSquareBracket));
                    break;
                case ']':
                    _tokenLine.Add(new Token("", TokenType.CloseSquareBracket));
                    break;
                default:
                    if (char.IsLetter(formattedB)) ParseIdentifierFromBytes();
                    if (char.IsNumber(formattedB)) ParseNumberFromBytes(); // string has been expanded so no number begins or ends with '.'
                    break;
            }

        }

        // add two EOF tokens just for robustness. one past the array may be an index out of bounds, but it IS still the end of the file
        _tokenLine.Add(new Token("", TokenType.EOF));
        _tokenLine.Add(new Token("", TokenType.EOF));

        return _tokenLine.ToArray();
    }

    void ParseMarkerFromBytes()
    {
        // loop just like text but make marker token instead

        string stringValue = "";

        _tokenIndex++;

        for (; _tokenIndex < _byteLine.Length; _tokenIndex++)
        {
            byte b = _byteLine[_tokenIndex];
            char formattedB = (char)(_vm.CharacterSetValues[b] - 224);

            if (!char.IsLetter(formattedB))
            {
                _tokenIndex--;
                break;
            }

            stringValue += _vm.CharacterSetValues[b];
        }

        _tokenLine.Add(new Token(stringValue, TokenType.Marker));
    }

    void ParseNumberFromBytes()
    {
        string stringValue = "";

        for (; _tokenIndex < _byteLine.Length; _tokenIndex++)
        {
            byte b = _byteLine[_tokenIndex];
            char formattedB = (char)(_vm.CharacterSetValues[b] - 224);

            if (!char.IsNumber(formattedB) && !formattedB.Equals('.'))
            {
                _tokenIndex--;
                break;
            }

            // stringValue += _vm.CharacterSetValues[b];
            stringValue += formattedB; // TODO remove this for above
        }

        _tokenLine.Add(new Token(stringValue, TokenType.Number));
    }

    void ParseIdentifierFromBytes()
    {
        string stringValue = "";

        for (; _tokenIndex < _byteLine.Length; _tokenIndex++)
        {
            byte b = _byteLine[_tokenIndex];
            char formattedB = (char)(_vm.CharacterSetValues[b] - 224);

            if (formattedB.Equals('?') || formattedB.Equals('$') || formattedB.Equals('%')) // add them now since they are not letters
            {
                stringValue += _vm.CharacterSetValues[b];
            }

            if (!char.IsLetter(formattedB))
            {
                _tokenIndex--;
                break;
            }

            stringValue += _vm.CharacterSetValues[b];
        }

        _tokenLine.Add(new Token(stringValue, TokenType.Identifier));
    }

    void ParseTextFromBytes()
    {
        _tokenIndex++; // move over quote

        string stringValue = "";

        for (; _tokenIndex < _byteLine.Length; _tokenIndex++)
        {
            byte b = _byteLine[_tokenIndex];
            char formattedB = (char)(_vm.CharacterSetValues[b] - 224);

            if (formattedB.Equals('\"')) break;

            stringValue += _vm.CharacterSetValues[b];
        }

        _tokenLine.Add(new Token(stringValue, TokenType.Text));
    }

    public struct Token
    {
        public string StringValue;
        public TokenType TokenType;

        public Token(string stringValue, TokenType tokenType)
        {
            StringValue = stringValue;
            TokenType = tokenType;
        }
    }

    // ------------------------------------------------------- //

    // TODO rebuild once full instruction set is made 
    // tokens byte data is the same as their enum value if above 128,
    // if below they are either not stored or are represented by the characters they consist of (stored as the byte form of raw screen text)
    public enum TokenType
    {
        // stored as ascii in bytes
        Identifier, Number, Text, Marker,

        //metadata
        ILLEGAL, EOF,

        //* starts at a number that does not interfere with text. Simple should not confuse tokens with ascii

        // Prefix expressions (single operand)
        Asc = 128, Chr, Len, Slc, Abs, Sin, Cos, Tan, Sgn, Sqr, Flr, Cel, Exp,

        // Prefix (no parenthesis)
        Get, Key, Parse, Time, Read,

        // Expressions (no operand)
        Fre, Rnd,

        // statements
        If, Then, For, To, Step, Next, Not, Reset, Goto, Gosub, Return, End, Write, On, Com, Let, Data, Query, Wait, Dim,
        Save, Load, List, New, Dir, Run, Stop, Cont, Delete, Csr, Print, Clear, Path, Renumber,
        Comma, Colon, OpenBracket, CloseBracket, OpenSquareBracket, CloseSquareBracket,

        // type identifiers
        Percent, Dollar, At,

        // operators
        Plus, Minus, Multiply, Divide, Equals, Power,

        // comparison operators
        Or, And, EqualTo, NotEqualTo, Less, Greater, LessEqual, GreaterEqual,

        True, False, Str, Val,
    }

    // index into this with the value of the tokentype - 128 to get its string equivalent
    // meaning this MUST be in identical order to the tokentype enum
    public readonly string[] KeywordStrings =
    {
        "asc", "chr$", "len", "slc$", "abs", "sin", "cos", "tan", "sgn", "sqr", "flr", "cel", "exp",

        "get", "key", "parse", "time", "read",

        "fre", "rnd",

        "if", "then", "for", "to", "step", "next", "not", "reset", "goto", "gosub", "return", "end", "write", "on", "com", "let", "data", "query", "wait", "dim",
        "save", "load", "list", "new", "dir", "run", "stop", "cont", "delete", "csr", "print", "clear", "path", "renumber",
        ",", ":", "(", ")", "[", "]",

        "%", "$", "@",

        "+", "-", "*", "/", "=", "^",

        "or", "and", "==", "<>", "<", ">", "<=", ">=",

        "true", "false", "str", "val",
    };

    // dictionary matching the string values of tokens and their corresponding token type, string values expect the return type identifier (% or $)
    Dictionary<string, TokenType> _keyWords = new()
    {
        // Prefix expressions (single operand)
        { "asc", TokenType.Asc },
        { "chr$", TokenType.Chr },
        { "len", TokenType.Len },
        { "slc$", TokenType.Slc },
        { "abs", TokenType.Abs },
        { "sin", TokenType.Sin },
        { "cos", TokenType.Cos },
        { "tan", TokenType.Tan },
        { "sgn", TokenType.Sgn },
        { "sqr", TokenType.Sqr },
        { "flr", TokenType.Flr },
        { "cel", TokenType.Cel },
        { "exp", TokenType.Exp },

        // Prefix (no parenthesis)
        { "get", TokenType.Get },
        { "key", TokenType.Key },
        { "parse", TokenType.Parse },
        { "time", TokenType.Time },
        { "read", TokenType.Read },

        // Expressions (no operand)
        { "fre", TokenType.Fre },
        { "rnd", TokenType.Rnd },

        // statements
        { "if", TokenType.If },
        { "then", TokenType.Then },
        { "for", TokenType.For },
        { "to", TokenType.To },
        { "step", TokenType.Step },
        { "next", TokenType.Next },
        { "not", TokenType.Not },
        { "reset", TokenType.Reset },
        { "goto", TokenType.Goto },
        { "gosub", TokenType.Gosub },
        { "return", TokenType.Return },
        { "end", TokenType.End },
        { "write", TokenType.Write },
        { "on", TokenType.On },
        { "com", TokenType.Com },
        { "let", TokenType.Let },
        { "data", TokenType.Data },
        { "save", TokenType.Save },
        { "load", TokenType.Load },
        { "list", TokenType.List },
        { "new", TokenType.New },
        { "dir", TokenType.Dir },
        { "run", TokenType.Run },
        { "stop", TokenType.Stop },
        { "cont", TokenType.Cont },
        { "delete", TokenType.Delete },
        { "csr", TokenType.Csr },
        { "print", TokenType.Print },
        { "clear", TokenType.Clear },
        { "path", TokenType.Path },
        { "renumber", TokenType.Renumber },
        { "or", TokenType.Or},
        { "and", TokenType.And},
        { "true", TokenType.True},
        { "false", TokenType.False },
        { "dim", TokenType.Dim },
        { "query", TokenType.Query },
        { "wait", TokenType.Wait },
        { "str$", TokenType.Str },
        { "val", TokenType.Val },





        //* for the real retro fans, they can still use PEEK and POKE instead of READ and WRITE
        // note no other keywords have multiple names
        // also, if in a program, these will be listed back out as READ and WRITE
        { "peek", TokenType.Read },
        { "poke", TokenType.Write },


    };

    // shorthand abbreviations for basic keywords. read on the screen as the shorthand but stored and listed as their full keyword
    Dictionary<string, string> _shorthands = new()
    {
        // Prefix expressions (single operand)
        { "aS", "asc" },
        { "cH", "chr$" },
        { "lE", "len" },
        { "sL", "slc$" },
        { "aB", "abs" },
        { "sI", "sin" },
        { "cO", "cos" },
        { "tA", "tan" },
        { "sG", "sgn" },
        { "sQ", "sqr" },
        { "fL", "flr" },
        { "cE", "cel" },
        { "eX", "exp" },
        { "vA", "val" },
        //? no STR$ shorthand


        // Prefix (no parenthesis)
        { "gE", "get" },
        { "kE", "key" },
        { "pA", "parse" },
        { "tI", "time" },
        { "rE", "read" },

        // Expressions (no operand)
        { "fR", "fre" },
        { "rN", "rnd" },

        // Statements
        //? no IF abbreviation
        { "tH", "then" },
        { "fO", "for" },
        //? No TO abbreviation
        { "stE", "step" },
        { "nE", "next" },
        { "nO", "not" },
        { "reS", "reset" },
        { "gO", "goto" },
        { "goS", "gosub" },
        { "reT", "return" },
        { "eN", "end" },
        { "wR", "write" },
        //? no ON abbreviation
        //? COM shorthand handled seperately
        //? no LET abbreviation
        { "sA", "save" },
        { "lO", "load" },
        { "lI", "list" },
        //? No NEW abbreviation
        { "dI", "dir" },
        { "rU", "run" },
        { "sT", "stop" },
        { "coN", "cont" },
        { "dE", "delete" },
        { "cS", "csr" },
        //? PRINT shorthand handles seperately
        { "cL", "clear" },
        { "paT", "path" },
        { "reN", "renumber" },
        //? No OR abbreviation
        { "aN", "and" },
        //? no TRUE or FALSE abbreviation
        //? no DIM abbreviation
        { "qU", "query" }

    };

}
