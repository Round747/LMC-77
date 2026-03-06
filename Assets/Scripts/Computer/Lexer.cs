using UnityEngine;
using Types;
using System.Collections.Generic;
using System;

public class Lexer : MonoBehaviour
{
    // TODO method to convert ram bytes to simpleline struct, only called if the program was edited by the user

    // Converts screen text from the textfield into a simpleline struct that contains
    // all the operations, tokens and bytes of that single line

    //@ TokeniseInPutString 
    // takes screen text and creates a simpleline struct,
    // it reads the line and expands all shorthands, editing the final string value

    // if there is no line number, it is executed immediately
    // if there is a line number, the simpleline struct is submitted to the simple program dictionary and placed into ram
    // if there is a line number but no content, the corresponding line is deleted from the program dictionary and the program is re-written to ram

    //@ ByteTokeniseString
    // takes the final screen text edited by TokeniseInputString and creates a byte[] for the simpleline struct. 
    // it only tokenises keywords and leaves the rest as ascii

    // ---------------------------------------------- //

    [SerializeField] VirtualMachine _vm;
    [SerializeField] Simple _simple;

    readonly char[] _unformattedWhitespace = new char[] { ' ', (char)(' ' + 128) };

    int _stringIndex;
    SimpleLine _currentLine;
    SimpleOperation _currentOperation;

    //* line strings
    string _inputString; // raw screen text
    string _formattedString; // reversed and unreversed shifted down to ascii
    string _unFormattedString; // unreversed shifted down to ascii, reversed not raw but still illegible
                               // used with UnReversedCharacter(char) to see if a reversed character is its ascii equivalent

    public void TokeniseInputString(string inputString)
    {
        // delete leading and trailing white space (adding one space at the end so parsing works correctly)
        EditLineStrings(ScreenTextToUnformattedString(inputString).TrimStart(_unformattedWhitespace).TrimEnd(_unformattedWhitespace) + " ");

        _currentLine = new() // initialise the line with an operation and a token list.
        {
            Operations = new()
            {
                new SimpleOperation { Tokens = new() }
            },
            StringValue = _inputString,
            LineNumber = ""
        };

        _currentOperation = _currentLine.Operations[0]; // make the first operation the globaly accessible one
        _stringIndex = 0;

        if (char.IsNumber(_formattedString[0])) ParseLineNumber(); // get the line number, if there is one

        while (_stringIndex < _inputString.Length) // loop through all the characters
        {
            char currentChar = _formattedString[_stringIndex];

            switch (currentChar)
            {
                case '<':
                    char peekCL = _formattedString[_stringIndex + 1]; // looks at the next character to see if it is a two character token
                    if (peekCL.Equals('>'))
                    {
                        _stringIndex++;
                        CreateToken(TokenType.NotEqualTo, "<>");
                    }
                    else if (peekCL.Equals('='))
                    {
                        _stringIndex++;
                        CreateToken(TokenType.GreaterEqual, "<=");
                    }
                    else CreateToken(TokenType.Less, "<");
                    break;
                case '>':
                    if (_formattedString[_stringIndex + 1].Equals('='))
                    {
                        _stringIndex++;
                        CreateToken(TokenType.GreaterEqual, ">=");
                    }
                    else CreateToken(TokenType.Greater, ">");
                    break;
                case '=':
                    if (_formattedString[_stringIndex + 1].Equals('='))
                    {
                        _stringIndex++;
                        CreateToken(TokenType.EqualTo, "==");
                    }
                    else CreateToken(TokenType.Equals, "=");
                    break;
                case '+':
                    CreateToken(TokenType.Plus, "+");
                    break;
                case '-':
                    CreateToken(TokenType.Minus, "-");
                    break;
                case '*':
                    CreateToken(TokenType.Multiply, "*");
                    break;
                case '/':
                    CreateToken(TokenType.Divide, "/");
                    break;
                case '(':
                    CreateToken(TokenType.OpenBracket, "(");
                    break;
                case ')':
                    CreateToken(TokenType.CloseBracket, ")");
                    break;
                case ',':
                    CreateToken(TokenType.Comma, ",");
                    break;
                case '%':
                    CreateToken(TokenType.Percent, "%");
                    break;
                case '$':
                    CreateToken(TokenType.Percent, "$");
                    break;
                case '@':
                    CreateToken(TokenType.At, "@");
                    break;
                case '^':
                    CreateToken(TokenType.Power, "^");
                    break;
                case ' ': // space, do nothing
                    break;
                case '\"':
                    ParseText();
                    break;
                case ':':
                    CreateToken(TokenType.EOF, "");
                    SimpleOperation newOperation = new() { Tokens = new() }; // make a new operation with a new token list and add it to the line
                    _currentLine.Operations.Add(newOperation);
                    _currentOperation = newOperation;
                    break;
                case '?':
                    CreateToken(TokenType.Print, "print"); // "?" shorthand for print
                    EditLineStrings(_unFormattedString.Remove(_stringIndex, 1).Insert(_stringIndex, "print"));
                    if (!_formattedString[_stringIndex + 1].Equals(' ')) EditLineStrings(_unFormattedString.Insert(_stringIndex, " ")); // if no space after, insert space
                    _stringIndex += 4; // skip past the full length of the keyword
                    break;
                case '#':
                    CreateToken(TokenType.Com, "com"); // "#" shorthand for comment
                    EditLineStrings(_unFormattedString.Remove(_stringIndex, 1).Insert(_stringIndex, "com"));
                    if (!_formattedString[_stringIndex + 1].Equals(' ')) EditLineStrings(_unFormattedString.Insert(_stringIndex, " ")); // if no space after, insert space
                    _stringIndex += 2; // skip past the full length of the keyword 
                    break;
                default: // not a symbol, must be a number or letter, else its not recognised and must be illegal
                    if (char.IsNumber(currentChar) || currentChar.Equals('.')) ParseNumber();
                    else if (char.IsLetter(currentChar)) ParseIdentifier();
                    else CreateToken(TokenType.ILLEGAL, currentChar.ToString());
                    break;
            }

            _stringIndex++;
        }

        CreateToken(TokenType.EOF, "");

        _currentLine.StringValue = _inputString; // save the final string value

        if (_currentLine.LineNumber == "") // execute line immediatly
        {
            print("execute");

            return;
        }
        else if (_currentLine.Operations[0].Tokens[0].Type == TokenType.EOF && _currentLine.Operations.Count == 1) // EOF is the first and only token
        {
            // TODO delete line matching line number
            print("delete");

            // _simple.DeleteLine(_currentLine.LineNumber); //! error here

            return;
        }

        // else add line to simple program

        print("store");

        if (int.Parse(_currentLine.LineNumber) > 63999) // line number too high
        {
            // TODO give error if line number too high
            print("?SYNTAX ERROR: line number too high");

            return;
        }

        _currentLine.Bytes = ByteTokeniseString(_inputString); // create line bytes
        // _simple.AddLine(_currentLine); //! error here


        DebugPrintTokens(_currentLine); // TODO remove this
        print(ScreenTextToFormattedString(_inputString));
    }

    void ParseText()
    {
        // TODO this parse assumes an ending quote exists and will index out the string

        string newToken = "";

        _stringIndex++; // move past the opening quote


        while (!_formattedString[_stringIndex].Equals('\"'))
        {
            if (_stringIndex == _formattedString.Length - 1) break; // at end of line and no closing quote

            newToken += _unFormattedString[_stringIndex];
            _stringIndex++;
        }

        // end the loop at the index of the last quote, this will skipped past after the encasing switch statement

        CreateToken(TokenType.Text, newToken);
    }

    void ParseNumber()
    {
        string newToken = "";
        // int startIndex = _stringIndex;

        if (_formattedString[_stringIndex].Equals('.')) // number started with a "." therefore interpreted as "0.XYZ"
        {
            newToken = "0"; // loop will add on full stop after

            if (!char.IsNumber(_formattedString[_stringIndex + 1])) // onlything inputed was a "."; make the token "0.0" and return
            {
                newToken = "0.0";
                CreateToken(TokenType.Number, newToken);
                // EditLineStrings(_unFormattedString.Remove(startIndex, 1).Insert(startIndex, "0.0"));
                // _stringIndex += 2;
                return;
            }
        }

        bool periodLastChar = false;

        while (char.IsNumber(_formattedString[_stringIndex]) || _formattedString[_stringIndex].Equals('.'))
        {
            char c = _formattedString[_stringIndex];

            if (periodLastChar) periodLastChar = false;
            if (c.Equals('.')) periodLastChar = true;

            newToken += c;

            _stringIndex++;
        }

        _stringIndex--; // index will be incremented after the switch statement

        if (periodLastChar) newToken += "0"; // number ends with a period like "0." therefore is treated as "0.0"

        CreateToken(TokenType.Number, newToken);
    }

    void ParseIdentifier()
    {
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

        if (hasCaps && _shorthands.TryGetValue(totalToken, out SimpleToken keyword)) // caps make a shorthand
        {
            _currentOperation.Tokens.Add(keyword); // tpken already created, just add to operation
            EditLineStrings(_unFormattedString.Remove(startIndex, totalToken.Length).Insert(startIndex, keyword.Value));

            _stringIndex = startIndex + keyword.Value.Length - 1; // move index past full keyword
        }
        else if (hasCaps && !_shorthands.TryGetValue(totalToken, out _)) // caps dont make a shorthand, remove to make an identifier
        {
            CreateToken(TokenType.Identifier, lowerToken);
            EditLineStrings(_unFormattedString.Remove(startIndex, totalToken.Length).Insert(startIndex, lowerToken)); // remove the caps in the identifier

            _stringIndex = startIndex + lowerToken.Length - 1; // move index past the new keyword
        }
        else // no caps: is either an identifier or keyword
        {
            if (_keyWords.TryGetValue(totalToken, out TokenType type)) CreateToken(type, totalToken);
            else CreateToken(TokenType.Identifier, totalToken);

            _stringIndex--; // index will be incremented after the switch statement
        }


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
            EditLineStrings(_unFormattedString.Insert(0, "0")); // add deleted zero back in
            _stringIndex++;
        }

        _currentLine.LineNumber = lineNumber;

        if (!_formattedString[_stringIndex].Equals(' ')) EditLineStrings(_unFormattedString.Insert(_stringIndex, " ")); // insert space after line number if none
    }

    void CreateToken(TokenType type, string value)
    {
        SimpleToken Token = new(value, type);
        _currentOperation.Tokens.Add(Token);
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

    //@ SCREEN TEXT ----> BYTES FOR STRUCT

    int _bstringIndex = 0;
    string _bformattedString;
    string _binputString;
    bool _bquotes = false;

    List<byte> _lineBytes;

    // TODO whenever a line is added, call this function in a loop to recreate the bytes for the program and place it into ram

    // commands are tokenised to 1 byte, numbers, text and identifiers are stored as the bytes of their ascii text
    // called when a simple line needs to be stored in memory
    public byte[] ByteTokeniseString(string inputString)
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

        _bformattedString = _bformattedString.Remove(0, _currentLine.LineNumber.Length); // delete line number from string
        _binputString = _binputString.Remove(0, _currentLine.LineNumber.Length);

        // trim leading whitespace 
        _bformattedString = _bformattedString.TrimStart(_unformattedWhitespace);
        _binputString = _binputString[^_bformattedString.Length..];

        _bstringIndex = 0;

        print(_bformattedString);

        while (_bstringIndex < _bformattedString.Length - 1) // skip last byte as it's always a space
        {
            char character = _bformattedString[_bstringIndex];

            if (character.Equals('\"')) _bquotes = !_bquotes;

            if (char.IsLetter(character) && !_bquotes)
            {
                ParseByteIdentifier();
            }
            else AddToBytes(_binputString[_bstringIndex]);

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

    // parses text not encased in quotes, doesnt check for caps since they have been removed by the normal lexer
    void ParseByteIdentifier()
    {
        string keyword = "";

        while (char.IsLetter(_bformattedString[_bstringIndex]))
        {
            keyword += _bformattedString[_bstringIndex];
            _bstringIndex++;
        }

        if (_keyWords.TryGetValue(keyword, out TokenType type)) _lineBytes.Add((byte)type); // if the text is a keyword, take the enums int value as a byte and insert to array
        else
        {
            foreach (char character in UnformattedStringToScreenText(keyword))
            {
                _lineBytes.Add((byte)Array.IndexOf(_vm.CharacterSetValues, character)); // add bytes to array
            }
        }

        _bstringIndex--;
    }



    // ------------------------------------------ //

    void DebugPrintTokens(SimpleLine line)
    {
        string Out = $"TOKENS\n\nLine number: {line.LineNumber} \n";
        foreach (SimpleOperation operation in line.Operations)
        {
            foreach (SimpleToken token in operation.Tokens)
            {
                Out += token.Type + ": " + token.Value + " \n";
            }

            Out += "::: Operation seperator ::: \n";
        }

        Out = Out[..^"::: Operation seperator ::: \n".Length];

        print(Out);
    }

    // -------------------------------------------------------- //

    // Simple lines are split into operations, which are each performed individually when interpreted
    // these operations are divided by colons in byte data when gathered

    // SimpleLine                                   A = 3 : PRINT A
    //     |
    //     |---- SimpleOperation                    
    //     |           |
    //     |           |---- SimpleToken            A
    //     |           |
    //     |           |---- SimpleToken            =
    //     |           |
    //     |           |---- SimpleToken            3
    //     |
    //     |---- SimpleOperation                    :               (inserted only when placing bytes)
    //     |           |
    //     |           |---- SimpleToken            PRINT
    //     |           |
    //     |           |---- SimpleToken            A

    public struct SimpleLine
    {
        public string LineNumber;

        public string StringValue;
        public byte[] Bytes;

        public List<SimpleOperation> Operations; //list of all parts of the line seperated by a ":". I.e. 'a=3 : print a' is two operations in one line

        public SimpleLine(string stringValue)
        {
            LineNumber = "";
            StringValue = stringValue;
            Operations = new();
            Bytes = null;
        }
    }

    public struct SimpleOperation
    {
        public List<SimpleToken> Tokens;
        // public string StringValue;
    }

    public struct SimpleToken
    {
        public TokenType Type;
        public string Value;

        public SimpleToken(string value, TokenType type)
        {
            Value = value;
            Type = type;
        }
    }

    // TODO rebuild once full instruction set is made 
    // tokens byte data is the same as their enum value if above 128,
    // if below they are either not stored or are represented by the characters they consist of (stored as the byte form of raw screen text)
    public enum TokenType
    {
        // stored as ascii in bytes
        Identifier, Number, Text,

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
        If, Then, For, To, Step, Next, Not, Reset, Goto, Gosub, Return, End, Write, On, Com, Let,
        Save, Load, List, New, Dir, Run, Stop, Cont, Delete, Csr, Print, Clear, Path, Renumber,
        Comma, Colon, OpenBracket, CloseBracket,

        // type identifiers
        Percent, Dollar, At,

        // operators
        Plus, Minus, Multiply, Divide, Equals, Power,

        // comparison operators
        Or, And, EqualTo, NotEqualTo, Less, Greater, LessEqual, GreaterEqual
    }

    // index into this with the value of the tokentype - 128 to get its string equivalent
    public string[] KeywordStrings =
    {
        "asc", "chr$", "len", "slc$", "abs", "sin", "cos", "tan", "sgn", "sqr", "flr", "cel", "exp",

        "get", "key", "parse", "time", "read",

        "fre", "rnd%",

        "if", "then", "for", "to", "step", "next", "not", "reset", "goto", "gosub", "return", "end", "write", "on", "com", "let",
        "save", "load", "list", "new", "dir", "run", "stop","cont", "delete", "csr", "print", "clear", "path", "renumber",
        ",", ":", "(", ")",

        "%", "$", "@",

        "+", "-", "*", "/", "=", "^",

        "or", "and", "==", "<>", "<", ">", "<=", ">=",
    };

    // TODO maybe remove the % requirement (almost always % so just a waste of time)

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
        { "and", TokenType.And}
    };

    // shorthand abbreviations for basic keywords. read on the screen as the shorthand but stored and listed as their full keyword
    Dictionary<string, SimpleToken> _shorthands = new()
    {
        // Prefix expressions (single operand)
        { "aS", new("asc", TokenType.Asc) },
        { "cH", new("chr$", TokenType.Chr) },
        { "lE", new("len", TokenType.Len) },
        { "sL", new("slc$", TokenType.Slc) },
        { "aB", new("abs", TokenType.Abs) },
        { "sI", new("sin", TokenType.Sin) },
        { "cO", new("cos", TokenType.Cos) },
        { "tA", new("tan", TokenType.Tan) },
        { "sG", new("sgn", TokenType.Sgn) },
        { "sQ", new("sqr", TokenType.Sqr) },
        { "fL", new("flr", TokenType.Flr) },
        { "cE", new("cel", TokenType.Cel) },
        { "eX", new("exp", TokenType.Exp) },

        // Prefix (no parenthesis)
        { "gE", new("get", TokenType.Get) },
        { "kE", new("key", TokenType.Key) },
        { "pA", new("parse", TokenType.Parse) },
        { "tI", new("time", TokenType.Time) },
        { "rE", new("read", TokenType.Read) },

        // Expressions (no operand)
        { "fR", new("fre", TokenType.Fre) },
        { "rN", new("rnd", TokenType.Rnd) },

        // Statements
        //? no IF abbreviation
        { "tH", new("then", TokenType.Then) },
        { "fO", new("for", TokenType.For) },
        //? No TO abbreviation
        { "stE", new("step", TokenType.Step) },
        { "nE", new("next", TokenType.Next) },
        { "nO", new("not", TokenType.Not) },
        { "reS", new("reset", TokenType.Reset) },
        { "gO", new("goto", TokenType.Goto) },
        { "goS", new("gosub", TokenType.Gosub) },
        { "reT", new("return", TokenType.Return) },
        { "eN", new("end", TokenType.End) },
        { "wR", new("write", TokenType.Write) },
        //? no ON abbreviation
        //? COM shorthand handled seperately
        //? no LET abbreviation
        { "sA", new("save", TokenType.Save) },
        { "lO", new("load", TokenType.Load) },
        { "lI", new("list", TokenType.List) },
        //? No NEW abbreviation
        { "dI", new("dir", TokenType.Dir) },
        { "rU", new("run", TokenType.Run) },
        { "sT", new("stop", TokenType.Stop) },
        { "coN", new("cont", TokenType.Cont) },
        { "dE", new("delete", TokenType.Delete) },
        { "cS", new("csr", TokenType.Csr) },
        //? PRINT shorthand handles seperately
        { "cL", new("clear", TokenType.Clear) },
        { "paT", new("path", TokenType.Path) },
        { "reN", new("renumber", TokenType.Renumber) },
        //? No OR abbreviation
        { "aN", new("and", TokenType.And) }
    };

}
