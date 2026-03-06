using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Types;
using UnityEngine;
using UnityEngine.InputSystem;


// shorthand
using TokenType = Tokeniser.TokenType;


public class Interpreter : MonoBehaviour
{
    [SerializeField] KeyboardAndScreenEditor _keyboard;
    [SerializeField] VirtualMachine _vm;
    [SerializeField] Tokeniser _tokeniser;
    [SerializeField] Simple _simple;
    [SerializeField] Parser _parser;
    [SerializeField] CursorPosition _cursor;

    public Tokeniser.Token[] CurrentLine;
    public int CurrentLineIndex;

    public bool IsProgramRunning;

    TokenType _operationSeperator; // what to expect between operations, default is ":", but can be changed to things like "THEN" depending on the command

    //* program timing

    // some lines can end before executing all tokens. The computer should only spend from the budget what it actually executed
    public int TokensExecuted = 0;

    public float TotalBudget = 0;
    public readonly int BudgetIncreasePerSecond = 1500; // determines absolute speed of SIMPLE program execution. This is how much token cost it can spend per second, most tokens cost more than 1

    void OnEnable()
    {
        LMCInputManager.BreakPressed += Break;
    }

    void OnDisable()
    {
        LMCInputManager.BreakPressed -= Break;
    }

    bool _breakPressed = false;

    void Break()
    {
        _breakPressed = true;
    }

    public IEnumerator InterpretSingleLine(Tokeniser.Token[] tokenLine)
    {
        try
        {
            _keyboard.CarriageReturn(2);
            ExecuteLine(tokenLine, 0);
        }
        catch (InterpreterException simpleException) // errors in interpreting due to issues with users code
        {
            if ((simpleException.ErrorType.Length + simpleException.Message.Length + 2) > 40)
            {
                _keyboard.PrintFormattedTextToScreen($"{simpleException.ErrorType}:");
                _keyboard.PrintFormattedTextToScreen($"{simpleException.Message}");
            }
            else _keyboard.PrintFormattedTextToScreen($"{simpleException.ErrorType}: {simpleException.Message}");
        }
        catch (Exception realException) // actual errors in my code are handled differently
        {
            _keyboard.PrintFormattedTextToScreen($"{realException.Message}".ToLower());
            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen($"{realException.StackTrace[..110]}...".ToLower());
            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen("this is an actual error, please report.");

            Debug.LogError(realException);
        }

        yield return null; // TODO make actual timing

    }

    bool _endLine = false;

    public void ExecuteLine(Tokeniser.Token[] tokenLine, byte startIndex)
    {
        CurrentLine = tokenLine;
        CurrentLineIndex = startIndex;
        TokensExecuted = 0;
        _endLine = false;

        // loop over line, any command encountered will move the currentlineindex themselves, so by the next loop there should be a new command to parse
        while (CurrentLine[CurrentLineIndex].TokenType != TokenType.EOF)
        {
            print("Parsing operation starting at token " + CurrentLine[CurrentLineIndex].TokenType);

            _operationSeperator = TokenType.Colon;

            switch (CurrentLine[CurrentLineIndex].TokenType) // all keywords that can start a line or operation
            {
                case TokenType.Let:
                    Let(); // assignment with let: "let a = 1"
                    break;
                case TokenType.Identifier:
                    Let(); // assignment without let: "a = 1"
                    break;
                case TokenType.Marker:
                    if (CurrentLineIndex != 0) throw new InterpreterException("01 syntax error", "marker decleration must begin a line");
                    // the markers value is stored merely by its presence within the line. Nothing needs to be done except skip over it.
                    SkipToken();
                    break;
                case TokenType.Dim:
                    Dim();
                    break;
                case TokenType.Data: // TODO
                    break;
                case TokenType.Clear:
                    _simple.DeleteAllVariables();
                    SkipToken();
                    break;
                case TokenType.Parse:
                    break;
                case TokenType.Csr:
                    Csr();
                    break;
                case TokenType.Print:
                    Print();
                    break;
                case TokenType.Key:
                    RunInProgramOnly("key");
                    _endProgram = true;
                    _endLine = true;
                    StartCoroutine(Key());
                    break;
                case TokenType.Query:
                    RunInProgramOnly("query");
                    _endProgram = true;
                    _endLine = true;
                    StartCoroutine(Query());
                    break;
                case TokenType.Wait:
                    RunInProgramOnly("wait"); // TODO in program since it takes time to exectue and when its finished it runs the program. implement a way to continue operation of a line isntead of the program
                    _endProgram = true;
                    _endLine = true;
                    StartCoroutine(Wait());
                    break;
                case TokenType.Reset: // TODO
                    break;
                case TokenType.Goto:
                    Goto();
                    break;
                case TokenType.Gosub:
                    Gosub();
                    break;
                case TokenType.Return:
                    Return();
                    break;
                case TokenType.End:
                    End();
                    break;
                case TokenType.Stop:
                    Stop();
                    break;
                case TokenType.Cont:
                    Cont();
                    break;
                case TokenType.Write:
                    Write();
                    break;
                case TokenType.On: // TODO
                    break;
                case TokenType.If:
                    If();
                    break;
                case TokenType.For:
                    RunInProgramOnly("for"); // TODO fix this! this is just a patch since looping doesnt work currently. Fix this then remove
                    For();
                    break;
                case TokenType.Next:
                    Next();
                    break;
                case TokenType.Save:
                    _endProgram = true;
                    _endLine = true;
                    StartCoroutine(Save());
                    break;
                case TokenType.Load:
                    _endProgram = true;
                    _endLine = true;
                    StartCoroutine(Load());
                    break;
                case TokenType.List:
                    StartCoroutine(ListProgram());
                    SkipToEndOfLine(); // move to end of line to halt execution. Line will be finished after program is run
                    break;
                case TokenType.New:
                    _simple.NewProgram();
                    break;
                case TokenType.Dir: // TODO
                    break;
                case TokenType.Run:
                    RunInDirectOnly("run");
                    _simple.DeleteAllVariables(); // all variables are wiped before running, but not after. CONT will not wipe variables hence why this method is placed here and not in the method
                    _vm.WriteByteToRam(_vm.StackPointer, 255); // same reasoning with resetting stack pointer
                    _continueAddress = _vm.ReadWordFromRam(_vm.pProgramSpaceStart); // reset cont, address not used
                    StartCoroutine(RunProgram(_vm.ReadWordFromRam(_vm.pProgramSpaceStart))); // start program at beginning
                    _endLine = true;
                    break;
                case TokenType.Delete: // TODO
                    break;
                case TokenType.Path:
                    Path();
                    break;
                case TokenType.Renumber: // TODO
                    break;
                case TokenType.Com:
                    // move to next operation
                    while (CurrentLine[CurrentLineIndex].TokenType != TokenType.Colon && CurrentLine[CurrentLineIndex].TokenType != TokenType.EOF)
                        CurrentLineIndex++;
                    break;
                default:
                    // started a line with a token that doesnt do anything
                    throw new InterpreterException("01 syntax error", "unexpected token");
            }
            if (_endLine) break;
            if (CurrentLine[CurrentLineIndex].TokenType != _operationSeperator && CurrentLine[CurrentLineIndex].TokenType != TokenType.EOF) throw new InterpreterException("01 syntax error", "unexpected token");
            if (CurrentLine[CurrentLineIndex].TokenType != TokenType.EOF) CurrentLineIndex++; // move over colon if not at end of line
        }

    }

    // ----------------------------------------------- //

    public int _currentLineNumber;
    int _nextLineAddress = -1; // invalid address means nothing wants to change the next line, follow line pointer. If its valid go there instead

    word _continueAddress; // address to resume operation at with CONT
    int _nextLineIndex = -1;

    bool _endProgram = false;


    // RUN
    public IEnumerator RunProgram(word startAddress) // TODO fix control flow in this method, its a bit jank with the while loops
    {
        // start at first address
        // tokenise line and execute it
        // change address for new line and loop

        IsProgramRunning = true;

        _keyboard.SetCanFlashCursor(false);

        word currentAddress = startAddress;

        TotalBudget = 0;

        _breakPressed = false;
        _endProgram = false;

        while (!_endProgram) // cpu loop
        {
            TotalBudget += Time.deltaTime * BudgetIncreasePerSecond; // total budget for the frame

            print(TotalBudget);

            try
            {
                while (TotalBudget > 0) // run as many lines as possible // TODO fix index starting
                {
                    if (_vm.ReadWordFromRam(currentAddress) == 65535) break; // end of program

                    _nextLineAddress = -1; // -1 means no method wants to change the next line
                    int index = _nextLineIndex == -1 ? 0 : _nextLineIndex; // if next line index wasnt set, make it 0, else use the new value
                    _nextLineIndex = -1; // -1 means it will start at index 0

                    _currentLineNumber = _vm.ReadWordFromRam(currentAddress + 2);
                    print($"executing line at line {_currentLineNumber} starting at index {index}");
                    ExecuteLine(_tokeniser.TokeniseLineBytes(GetLineBytes(currentAddress)), (byte)index);

                    if (_nextLineAddress != -1) currentAddress = _nextLineAddress; // a goto, gosub, or next has set the next line to go to. Honour that
                    else currentAddress = _vm.ReadWordFromRam(currentAddress); // else follow line pointer

                    TotalBudget -= CalculateLineTimePenalty(); // subtract line from budget 

                    if (_breakPressed || _endProgram) break;
                }

            }
            catch (InterpreterException simpleException) // errors in interpreting due to issues with users code
            {
                // TODO fix carriage returns?
                _keyboard.CarriageReturn(2);

                print("Message length:" + (simpleException.ErrorType.Length + simpleException.Message.Length) + ", " + 11.ToString());

                if ((simpleException.ErrorType.Length + simpleException.Message.Length + 11 + _currentLineNumber.ToString().Length) > 40) // +11 for characters like "at line"  
                {
                    _keyboard.PrintFormattedTextToScreen($"{simpleException.ErrorType} at line {_currentLineNumber}:");
                    _keyboard.PrintFormattedTextToScreen($"{simpleException.Message}");
                }
                else _keyboard.PrintFormattedTextToScreen($"{simpleException.ErrorType} at line {_currentLineNumber}: {simpleException.Message}");

                break; // exit program execution
            }
            catch (Exception realException) // actual errors in my code are handled differently
            {
                _keyboard.PrintFormattedTextToScreen($"{realException.Message} at line {_currentLineNumber}".ToLower());
                _keyboard.CarriageReturn();
                _keyboard.PrintFormattedTextToScreen($"{realException.StackTrace[..110]}...".ToLower());
                _keyboard.CarriageReturn();
                _keyboard.PrintFormattedTextToScreen("this is an actual error, please report.");

                Debug.LogError(realException);

                break; // exit program execution
            }

            if (_breakPressed)
            {
                _keyboard.CarriageReturn();

                while (TotalBudget < 10)
                {
                    TotalBudget += Time.deltaTime * BudgetIncreasePerSecond; // total budget for the frame
                    yield return null;
                }

                _keyboard.PrintFormattedTextToScreen($"break at line {_currentLineNumber}");
                _breakPressed = false;
                break;
            }

            if (_vm.ReadWordFromRam(currentAddress) == 65535) break; // TODO slightly scuffed, make way to break when at end of program

            yield return null; // done for the frame

        }

        if (!_endProgram)
        {
            IsProgramRunning = false;

            _keyboard.SetCanFlashCursor(true);

            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen($"ready");
        }
    }

    // PRINT Expression(Float / Int / String / Bool)
    public void Print() // TODO edit simple logical lines when writing
    {
        // TODO while not end of file or :, parse expression and print, before parsing, check for end as well to see hanging , or ;, if so cursor up twice to prevent carriage return
        SkipToken();

        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Colon || CurrentLine[CurrentLineIndex].TokenType == TokenType.EOF) // empty print statement. 
        {
            _keyboard.CarriageReturn();
            return;
        }

        bool endingCR = true;

        while (true)
        {

            if (IsEndOfOperation())
            {
                endingCR = false;
                break;
            }

            object parse = _parser.ParseExpression();

            string message; // assumed string value

            if (parse is not string)
            {
                print("converting to string");
                message = Parser.InfixOperator.ConvertLiteralToString(parse);

                if (parse is float f && f >= 0) message = KeyboardAndScreenEditor.Space + message; // add space in place of negative sign so it is aligned with negatives
            }
            else
            {
                print("is a string");
                message = (string)parse;
            }

            print(message);

            byte reverse = 0; // 0 if false, 128 if true, added to byte and char to change value

            for (int i = 0; i < message.Length; i++)
            {
                int index = (_vm.Ram[_vm.CursorPositionY] * _vm.ScreenWidth) + _vm.Ram[_vm.CursorPositionX];

                byte b = (byte)Array.IndexOf(_vm.CharacterSetValues, message[i]);

                if (b > 127) // control code
                {
                    switch (message[i])
                    {
                        case KeyboardAndScreenEditor.UpControlCharacter:
                            _keyboard.CursorUp(false);
                            break;
                        case KeyboardAndScreenEditor.DownControlCharacter:
                            _keyboard.CursorDown(false);
                            break;
                        case KeyboardAndScreenEditor.LeftControlCharacter:
                            _keyboard.CursorLeft(false);
                            break;
                        case KeyboardAndScreenEditor.RightControlCharacter:
                            _keyboard.CursorRight(false);
                            break;
                        case KeyboardAndScreenEditor.ReverseOnControlCharacter:
                            _vm.WriteBitToRam(_vm.TypeReversed, 0, true); // reverse on
                            reverse = 128; // reverse on, added to byte to shift upwards
                            break;
                        case KeyboardAndScreenEditor.ReverseOffControlCharacter:
                            _vm.WriteBitToRam(_vm.TypeReversed, 0, false); // reverse off
                            reverse = 0; // reverse off
                            break;
                        case KeyboardAndScreenEditor.RestoreControlCharacter:
                            _keyboard.Restore();
                            break;
                        case KeyboardAndScreenEditor.HomeControlCharacter:
                            _keyboard.CursorHome();
                            break;
                        case KeyboardAndScreenEditor.ClearControlCharacter:
                            _keyboard.ClearScreen();
                            break;
                        default:
                            throw new InterpreterException("05 illegal quantity error", "control code not recognised");
                    }
                }
                else // print the character to the screen
                {
                    _keyboard.TextField.text = _keyboard.TextField.text.Remove(index, 1).Insert(index, ((char)(message[i] + reverse)).ToString()); // insert character to textbox
                    _vm.WriteByteToRam(_vm.ReadWordFromRam(_vm.pScreenSpaceStart) + index, (byte)(b + reverse)); // place byte into ram
                    _keyboard.CursorRight(false);
                }

            }

            if (IsEndOfOperation()) break;
            else if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Comma)
            {
                SkipToken();
                _keyboard.CursorRight(false);
                continue;
            }
            else throw new InterpreterException("01 syntax error", "unexpected token");
        }

        if (endingCR) _keyboard.CarriageReturn();

        _keyboard.FlashCursor();
    }

    // [LET] variable(T) = Expression(T)
    void Let()
    {
        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Let) SkipToken(); // move over let if there

        Tokeniser.Token identifier = ExpectToken(TokenType.Identifier);
        string variableName;

        Parser.Atom variableAtom = new Parser.Atom(identifier); // only want a single identifier, not an expression, create an atom from the identifier
        variableName = variableAtom.GetVariableName();


        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.OpenSquareBracket) // TODO handle array assignment
        {
            SkipToken(); // skip over opening bracket

            List<word> indexes = new List<word>();

            while (CurrentLine[CurrentLineIndex].TokenType != TokenType.CloseSquareBracket) // loop over all possible dimensions
            {
                float dimensionExpression = ExpectFloatExpression();

                if (CurrentLine[CurrentLineIndex].TokenType != TokenType.Comma && CurrentLine[CurrentLineIndex].TokenType != TokenType.CloseSquareBracket) throw new InterpreterException("01 syntax error", "unexpected token");
                if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Comma) SkipToken(); // skip over comma

                if (dimensionExpression < 0 || dimensionExpression > 65535) throw new InterpreterException("05 illegal quanitity error", "argument outside of allowable range");

                indexes.Add((int)dimensionExpression); // add dimensionlength to list
            }

            SkipToken(); // discard closing bracket

            ExpectToken(TokenType.Equals);

            object arrayExpressionValue = _parser.ParseExpression();

            switch (variableAtom.GetDataType())
            {
                case '%':
                    if (arrayExpressionValue is not float iE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                    _simple.SetIntArray(variableName, indexes.ToArray(), iE);
                    break;
                case '$':
                    if (arrayExpressionValue is not string sE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                    _simple.SetStringArray(variableName, indexes.ToArray(), sE);
                    break;
                case '?':
                    if (arrayExpressionValue is not bool bE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                    _simple.SetBoolArray(variableName, indexes.ToArray(), bE);
                    break;
                default: // last character of name is part of the name (no type identifier)
                    if (arrayExpressionValue is not float fE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                    _simple.SetFloatArray(variableName, indexes.ToArray(), fE);
                    break;
            }

            return;
        }


        ExpectToken(TokenType.Equals);

        object expressionValue = _parser.ParseExpression();

        switch (variableAtom.GetDataType())
        {
            case '%':
                if (expressionValue is not float iE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                _simple.SetIntVariable(variableName, iE);
                break;
            case '$':
                if (expressionValue is not string sE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                _simple.SetStringVariable(variableName, sE);
                break;
            case '?':
                if (expressionValue is not bool bE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                _simple.SetBoolVariable(variableName, bE);
                break;
            default: // last character of name is part of the name (no type identifier)
                if (expressionValue is not float fE) throw new InterpreterException("02 type mismatch error", "incorrect datatype");
                _simple.SetFloatVariable(variableName, fE);
                break;
        }
    }




    // DIM variable(T)[expression(float / int), ...(repeat for dimensions)]
    void Dim()
    {
        SkipToken(); // move over dim

        Tokeniser.Token identifier = ExpectToken(TokenType.Identifier);
        string variableName;

        Parser.Atom variableAtom = new Parser.Atom(identifier); // only want a single identifier, not an expression, create an atom from the identifier

        variableName = variableAtom.GetVariableName();

        ExpectToken(TokenType.OpenSquareBracket);

        List<word> dimensionLengths = new List<word>();

        while (CurrentLine[CurrentLineIndex].TokenType != TokenType.CloseSquareBracket) // loop over all possible dimensions
        {
            float dimensionExpression = ExpectFloatExpression();

            if (CurrentLine[CurrentLineIndex].TokenType != TokenType.Comma && CurrentLine[CurrentLineIndex].TokenType != TokenType.CloseSquareBracket) throw new InterpreterException("01 syntax error", "unexpected token");
            if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Comma) SkipToken(); // skip over comma

            if (dimensionExpression < 1 || dimensionExpression > 65535) throw new InterpreterException("05 illegal quanitity error", "argument outside of allowable range");

            dimensionLengths.Add((int)dimensionExpression); // add dimensionlength to list
        }

        SkipToken(); // discard closing bracket

        switch (variableAtom.GetDataType())
        {
            case '%':
                _simple.CreateIntArray(variableName, dimensionLengths.ToArray());
                break;
            case '$':
                _simple.CreateStringArray(variableName, dimensionLengths.ToArray());
                break;
            case '?':
                _simple.CreateBoolArray(variableName, dimensionLengths.ToArray());
                break;
            default: // last character of name is part of the name (no type identifier)
                _simple.CreateFloatArray(variableName, dimensionLengths.ToArray());
                break;
        }
    }

    // IF Expression(bool) THEN Statement
    void If()
    {
        SkipToken();

        bool condition = ExpectBoolExpression();

        if (condition)
        {
            if (CurrentLine[CurrentLineIndex].TokenType != TokenType.Then) throw new InterpreterException("01 syntax error", "if without then");
            _operationSeperator = TokenType.Then; // tell executeLine to expect this token on the start on the start of the next operation 

            // continue execution as normal, executeline will execute following operations
        }
        else CurrentLineIndex = CurrentLine.Length - 1; // move index to end of file token, which will cause executeLine to end execution there
    }

    // GOTO Expression(float / Int / marker)
    void Goto()
    {
        SkipToken();
        Dictionary<word, word> lineAddresses = _simple.GetAllLines();
        word gotoLineAddress;


        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Marker)
        {
            gotoLineAddress = lineAddresses[(word)_simple.GetMarkerVariable(CurrentLine[CurrentLineIndex].StringValue)]; // get marker line number and get its address from the dictionary
        }
        else // is an expression (number literal or variable)
        {
            float expression = ExpectFloatExpression();

            if (lineAddresses.TryGetValue((word)expression, out word lineAddress)) gotoLineAddress = lineAddress; // expression represents a line number
            else throw new InterpreterException("05 illegal quantity error", "line number does not exist");
        }

        _nextLineAddress = gotoLineAddress;

        SkipToEndOfLine(); // rest of line isnt exectued because of the jump
    }

    // GOSUB Expression(float / Int / marker)
    void Gosub()
    {
        // get expression or marker value
        // set next line to that line (if it exists)

        Dictionary<word, word> lineAddresses = _simple.GetAllLines();
        word currentLineAddress = lineAddresses[_currentLineNumber]; // get address from line number
        word gosubLineAddress;

        SkipToken();

        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Marker)
        {
            gosubLineAddress = lineAddresses[(word)_simple.GetMarkerVariable(CurrentLine[CurrentLineIndex].StringValue)]; // get marker line number and get its address from the dictionary
            SkipToken(); // move over marker
        }
        else // is an expression (number literal or variable)
        {
            float expression = ExpectFloatExpression();

            if (lineAddresses.TryGetValue((word)expression, out word lineAddress)) gosubLineAddress = lineAddress; // expression represents a line number
            else throw new InterpreterException("05 illegal quantity error", "line number does not exist");
        }

        _nextLineAddress = gosubLineAddress; // tell program to move to gosubs line next

        // parse skipped over expression, it now sits after it
        // there are tokens after the gosub statement that arent seperated by a colon. RETURN will move after these so we need to check its valid here.
        if (CurrentLine[CurrentLineIndex].TokenType != TokenType.Colon && CurrentLine[CurrentLineIndex].TokenType != TokenType.EOF) throw new InterpreterException("01 syntax error", "unexpected token");
        SkipToken(); // move over colon

        byte currentTokenIndex = (byte)CurrentLineIndex; // where RETURN should start within the line. The beginning of the next operation

        byte[] stackEntry = new byte[3];

        stackEntry[0] = currentTokenIndex; // gosub identifier, so return knows its grabbing the right stack entry, most significant bit not set which just means it holds the value of the token index. It enver exceeds 80 so it is never set by the index
        stackEntry[1] = currentLineAddress.Lo;
        stackEntry[2] = currentLineAddress.Hi;

        _vm.PushBytesToStack(stackEntry);

        SkipToEndOfLine(); // skip line and move to one set by gosub
    }

    // RETURN
    void Return()
    {
        byte[] stackEntry;

        // only error that this method can throw is a "stack underflow" which would only occur if a return doesnt have a return address to pull from the stack
        try { stackEntry = _vm.PullBytesFromStack(3); }
        catch
        {
            throw new InterpreterException("06 return without gosub error", ""); // rethrowing the error to change the "stack underflow" message to the method specific one
        }

        if ((stackEntry[0] & 0b10000000) != 0) throw new InterpreterException("06 return without gosub error", ""); // the retrieved stack entry does not have the most significant bit unset, it is invalid

        word returnAddress = new word(stackEntry[1], stackEntry[2]);

        byte lineIndex = stackEntry[0]; // bit identifier isnt there so just take the bytes value

        _nextLineAddress = returnAddress;

        SkipToEndOfLine();

        _nextLineIndex = lineIndex;
    }

    // FOR variable(float) = expression(float) TO expression(float) [STEP expression(float)]
    void For()
    {
        SkipToken();

        // create an atom out of the identifier, if it is there
        Tokeniser.Token token = ExpectToken(TokenType.Identifier);
        Parser.Atom variableAtom = new Parser.Atom(token); // only want a single identifier, not an expression, create an atom from the identifier

        object variableValue = variableAtom.EvaluateExpression(_vm);
        if (variableValue is not float) throw new InterpreterException("02 type mismatch error", "expected float variable");

        _ = ExpectToken(TokenType.Equals);

        float variableNewValue = ExpectFloatExpression();

        _simple.SetFloatVariable(token.StringValue, variableNewValue); // execute "variable(float) = expression(float)"

        _ = ExpectToken(TokenType.To);

        float limitValue = ExpectFloatExpression();

        // step assumed to be 1 unless changed
        float stepValue = 1;

        // a step number has been specified
        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Step)
        {
            SkipToken();
            float step = ExpectFloatExpression();
            stepValue = step;
        }
        else if (CurrentLine[CurrentLineIndex].TokenType != TokenType.Colon && CurrentLine[CurrentLineIndex].TokenType != TokenType.EOF) throw new InterpreterException("01 syntax error", "unexpected token");

        byte loopLineIndex = (byte)(CurrentLineIndex + 1); // +1 to jump over the colon. If its the end of the line there are two EOF tokens so its fine
        // print($"index after for is {loopLineIndex}, at it is {CurrentLine[loopLineIndex].TokenType} and the line has a length of {CurrentLine.Length}");

        byte[] stackEntry = new byte[13];

        stackEntry[0] = (byte)(loopLineIndex | 128); // for loop byte identifier, so next knows its grabbing the right stack entry. 128 is most significant bit set, line index can never exceed 80 so they dont overlap

        stackEntry[1] = (byte)Array.IndexOf(_vm.CharacterSetValues, token.StringValue[0]); // first character of variable name
        stackEntry[2] = token.StringValue.Length == 2 ? (byte)Array.IndexOf(_vm.CharacterSetValues, token.StringValue[1]) : (byte)0; // second character of variable name, if it exists

        byte[] limitArray = BitConverter.GetBytes(limitValue); // limit value float to 4 bytes
        Array.Copy(limitArray, 0, stackEntry, 3, 4);

        byte[] StepArray = BitConverter.GetBytes(stepValue); // step value float to 4 bytes
        Array.Copy(StepArray, 0, stackEntry, 7, 4);

        word address = _simple.GetAllLines()[_currentLineNumber]; // get address from line number

        stackEntry[11] = address.Lo;
        stackEntry[12] = address.Hi;

        _vm.PushBytesToStack(stackEntry);
    }

    // NEXT [variable(float)]
    void Next()
    {
        SkipToken(); // move past NEXT

        byte[] stackEntry = _vm.PullBytesFromStack(13);

        if ((byte)(stackEntry[0] & 0b10000000) != 128) throw new InterpreterException("07 next without for error", ""); // what was pulled from the stack doesnt have the for loop identifier bit. stack entry is invalid

        string variableName = "";
        variableName += _vm.CharacterSetValues[stackEntry[1]];
        if (stackEntry[2] != 0) variableName += _vm.CharacterSetValues[stackEntry[2]]; // add the second character, if it exists

        if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Identifier) // variable name specified in NEXT
        {
            if (!variableName.Equals(CurrentLine[CurrentLineIndex].StringValue)) throw new InterpreterException("07 next witout for error", ""); // this is a for loop, but not the right one
            SkipToken(); // skip over variable
        }

        float step = BitConverter.ToSingle(stackEntry[7..11]); // get float value from bits

        _simple.SetFloatVariable(variableName, _simple.GetFloatVariable(variableName) + step); // add step to variable (X = X + step)

        float limit = BitConverter.ToSingle(stackEntry[3..7]); // get float value from bits

        if ((step > 0 && _simple.GetFloatVariable(variableName) > limit) || (step < 0 && _simple.GetFloatVariable(variableName) < limit)) // if loop end requirement has been met
        {
            return; // finished, move to operation after next
        }

        // else loop again

        word address = new word(stackEntry[11], stackEntry[12]);

        byte index = (byte)(stackEntry[0] - 128); //subtract most significant bit

        _vm.PushBytesToStack(stackEntry); // put byte back onto stack for next loop to use

        // go back to the token after the for loop
        _nextLineAddress = address;
        _nextLineIndex = index;
        SkipToEndOfLine();
    }

    // STOP
    void Stop()
    {
        _breakPressed = true; // stop program execution
        _continueAddress = _simple.GetAllLines()[_currentLineNumber]; // get address from line number

        SkipToken(); // move past STOP
        if (!IsTokenOfTypes(new TokenType[] { TokenType.Colon, TokenType.EOF })) throw new InterpreterException("01 syntax error", "unexpected token");

        _nextLineIndex = CurrentLineIndex + 1; // past the colon, starting at the next operation
        SkipToEndOfLine(); // program halted after line
    }

    void Cont()
    {
        StartCoroutine(RunProgram(_continueAddress)); // start program from where cont specified. Run program will jump to the index of _nextlineindex within
        SkipToEndOfLine(); // move to end of line to halt execution. Line will be finished after program is run
    }

    // goes to the end of the second last line so that the run loop will move to the end of the program and end itself
    // TODO is this the best approach if it prints a message once its done?
    void End()
    {
        word address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart);
        word lastAddress = address;

        while (_vm.ReadWordFromRam(address) != 65535)
        {
            lastAddress = address;
            address = _vm.ReadWordFromRam(address); // follow pointer to next line
        }

        _nextLineAddress = lastAddress;

        _nextLineIndex = _tokeniser.TokeniseLineBytes(GetLineBytes(lastAddress)).Length - 1; // goto end of next line as not to execute it
        SkipToEndOfLine();
    }

    // WRITE Expression(float)
    void Write()
    {
        object address;
        object value;

        SkipToken();

        (address, value) = _parser.ParseTwoExpressions();

        if (address is not float fA || value is not float fV) throw new InterpreterException("02 type mismatch error", "expected float expression");
        if (fA < 0 || fA > _vm.MemorySize) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");
        if (fV < 0 || fV > 255) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

        _vm.WriteByteToRam((int)fA, (byte)fV);

        _vm.RefreshComputer();
    }

    // CSR Expression(float), Expression(float)
    void Csr() // TODO finishing line execution causes a carriage return, setting x to 0 and increasing y.
    {
        object xPos;
        object yPos;

        SkipToken(); // skip csr

        (xPos, yPos) = _parser.ParseTwoExpressions();

        if (xPos is not float fX || yPos is not float fY) throw new InterpreterException("02 type mismatch error", "expecting float expression");
        if (fX > 39 || fX < 0 || fY > 24 || fY < 0) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

        _vm.WriteByteToRam(_vm.CursorPositionX, (byte)fX);
        _vm.WriteByteToRam(_vm.CursorPositionY, (byte)fY);

        _keyboard.FlashCursor(); // update visible cursor position
    }

    char _textKey;
    bool _textKeyPressed = false;
    bool _anyKeypressed = false;

    // KEY variable(float / string)
    IEnumerator Key() // TODO nextline index assumes a valid operation seperator?
    {
        SkipToken(); // skip key

        Tokeniser.Token variable = ExpectToken(TokenType.Identifier);

        Parser.Atom identifier = new Parser.Atom(variable); // create new atom from variable

        // TODO handle array variables

        if (identifier.GetDataType() == '$') // expect a text key
        {
            _textKeyPressed = false;
            Keyboard.current.onTextInput += WaitForTextKey;

            while (!_textKeyPressed)  // wait until a key is pressed, this will set the _textKey variable
            {
                if (_breakPressed) // the break key is only checked at the end of every line while the program is running. Check here since the coroutine hasnt started yet
                {
                    _keyboard.CarriageReturn();
                    _keyboard.PrintFormattedTextToScreen($"break at line {_currentLineNumber}");
                    IsProgramRunning = false;
                    _keyboard.SetCanFlashCursor(true);
                    Keyboard.current.onTextInput -= WaitForTextKey;

                    yield break;
                }
                yield return null;
            }

            Keyboard.current.onTextInput -= WaitForTextKey;

            // TODO handle array variables
            _simple.SetStringVariable(identifier.GetVariableName(), _textKey.ToString());

            if (!IsEndOfOperation()) throw new InterpreterException("01 syntax error", "unexpected token");

            _continueAddress = _vm.ReadWordFromRam(_simple.GetAllLines()[_currentLineNumber]);
            _nextLineIndex = CurrentLineIndex + 1;
            StartCoroutine(RunProgram(_simple.GetAllLines()[_currentLineNumber])); // start program from after key

            yield break;
        }
        else if (identifier.GetDataType() == '?') throw new InterpreterException("02 type mismatch error", "invalid datatype");

        _anyKeypressed = false;
        LMCInputManager.KeyBoardDown += WaitForAnyKey;

        while (!_anyKeypressed) yield return null;

        LMCInputManager.KeyBoardDown -= WaitForAnyKey;

        if (_breakPressed) // the break key is only checked at the end of every line while the program is running. Check here since the coroutine hasnt started yet
        {
            _keyboard.CarriageReturn();
            _keyboard.PrintFormattedTextToScreen($"break at line {_currentLineNumber}");
            IsProgramRunning = false;
            _keyboard.SetCanFlashCursor(true);

            yield break;
        }

        byte keyValue = _vm.Ram[_vm.KeyPressed];

        if (identifier.GetDataType() == '%') _simple.SetIntVariable(identifier.GetVariableName(), keyValue);
        else _simple.SetFloatVariable(identifier.GetVariableName(), keyValue); // else no datatype so a float

        RestartProgram();
    }

    void WaitForAnyKey() => _anyKeypressed = true;

    void WaitForTextKey(char key)
    {
        if (char.IsControl(key) || key.Equals(' ')) return;

        _textKey = (char)(key + 224); // shift up to screen text
        _textKeyPressed = true;
    }

    bool _enterPressed = false;

    // QUERY Expression(string), variable(string) [, variable(string) ,...]
    IEnumerator Query()
    {
        SkipToken(); // skip QUERY

        string message = ExpectStringExpression(); // message to be printed when asking for input
        _keyboard.PrintRawTextToScreen(message);

        _keyboard.SetCanFlashCursor(true); // show cursor to indicate expected input

        while (!IsEndOfOperation()) // TODO get variable and query for input. if number then compare input with tryparse
        {
            ExpectToken(TokenType.Comma);
            Parser.Atom variable = new Parser.Atom(ExpectToken(TokenType.Identifier)); // get variable to store input // TODO handle arrays 

            if (!variable.GetDataType().Equals('$')) throw new InterpreterException("02 type mismatch error", "expected string variable");

            // if (!IsTokenOfTypes(new TokenType[] { TokenType.Colon, TokenType.EOF, TokenType.Comma })) throw new InterpreterException("01 syntax error", "unexpected token");
            // if (CurrentLine[CurrentLineIndex].TokenType == TokenType.Comma) SkipToken();

            _vm.WriteByteArrayToRam(_vm.QueryInputBuffer, new byte[80]); // write 0 bytes to the input buffer to wipe it
            _vm.WriteByteToRam(_vm.QueryInputPointer, 0); // reset length pointer

            _keyboard.TypeRawCharacter((char)('*' + 224));

            while (_vm.Ram[_vm.KeyPressed] == Array.IndexOf(LMCInputManager.Instance.InputKeyCodes, KeyCode.Return)) yield return null; // dont submit the enter used to run the program


            Keyboard.current.onTextInput += GatherQueryInput;

            _enterPressed = false;


            while (!_enterPressed)
            {
                if (_breakPressed) // the break key is only checked at the end of every line while the program is running. Check here since the coroutine hasnt started yet
                {
                    _keyboard.CarriageReturn();
                    _keyboard.PrintFormattedTextToScreen($"break at line {_currentLineNumber}");
                    IsProgramRunning = false;
                    _keyboard.SetCanFlashCursor(true);
                    Keyboard.current.onTextInput -= GatherQueryInput;

                    yield break;
                }
                yield return null;
            }

            Keyboard.current.onTextInput -= GatherQueryInput;

            string result = "";

            // for the length of the inputted string, add convert to actual string
            for (int i = 0; i < _vm.Ram[_vm.QueryInputPointer]; i++) result += _vm.CharacterSetValues[_vm.Ram[_vm.QueryInputBuffer + i]];

            // if a number do tryparse, throw an error if it fails

            _simple.SetStringVariable(variable.GetVariableName(), result);

            _keyboard.CarriageReturn();
        }

        RestartProgram();
    }

    void GatherQueryInput(char key)
    {
        if (key.Equals('\n') || key.Equals('\r'))
        {
            _enterPressed = true;
            return;
        }

        if (key.Equals('\b'))
        {
            if (_vm.Ram[_vm.QueryInputPointer] <= 0) return; // nothing left to delete
            _keyboard.BackSpace();
            _vm.WriteByteToRam(_vm.QueryInputPointer, (byte)(_vm.Ram[_vm.QueryInputPointer] - 1)); // decrement input pointer
            _vm.WriteByteToRam(_vm.QueryInputBuffer + _vm.Ram[_vm.QueryInputPointer], 0); // delete char in input buffer
        }

        if (char.IsControl(key)) return;

        if (_vm.Ram[_vm.QueryInputPointer] == 80) return;

        byte keyValue = (byte)Array.IndexOf(_vm.CharacterSetValues, (char)(key + 224));

        _vm.WriteByteToRam(_vm.QueryInputBuffer + _vm.Ram[_vm.QueryInputPointer], keyValue); // add character to end of input buffer
        _vm.WriteByteToRam(_vm.QueryInputPointer, (byte)(_vm.Ram[_vm.QueryInputPointer] + 1)); // increment input pointer

        _keyboard.TypeRawCharacter((char)(key + 224));
    }


    // WAIT expression(float)
    IEnumerator Wait()
    {
        SkipToken();

        float timeToWait = ExpectFloatExpression();

        if (!IsEndOfOperation()) throw new InterpreterException("01 syntax error", "unexpected token");

        float timeWaited = 0;

        while (timeWaited < timeToWait)
        {
            if (_breakPressed) // the break key is only checked at the end of every line while the program is running. Check here since the coroutine hasnt started yet
            {
                _keyboard.CarriageReturn();
                _keyboard.PrintFormattedTextToScreen($"break at line {_currentLineNumber}");
                IsProgramRunning = false;
                _keyboard.SetCanFlashCursor(true);

                yield break;
            }
            timeWaited += Time.deltaTime;
            yield return null;
        }

        RestartProgram();
    }

    // --------------------- //

    void Path()
    {
        RunInDirectOnly("path");

        SkipToken(); // move over path

        string filePath = ExpectStringExpression();

        string formattedPath = "";
        foreach (char character in filePath) formattedPath += (char)(character - 224);

        // windows uses backslashes for file paths, mac uses forwards
        if (Application.platform == RuntimePlatform.WindowsPlayer || Application.platform == RuntimePlatform.WindowsEditor)
        {
            formattedPath = formattedPath.Replace('/', '\\');
            if (!formattedPath[0].Equals('\\')) formattedPath = formattedPath.Insert(0, "\\"); // if there is not starting slash to indicate a folder, add one
        }
        else if (Application.platform == RuntimePlatform.OSXPlayer)
        {
            formattedPath = formattedPath.Replace('\\', '/');
            if (!formattedPath[0].Equals('/')) formattedPath = formattedPath.Insert(0, "/"); // if there is not starting slash to indicate a folder, add one
        }
        _vm.DiskDrive.FilePath = _vm.DiskDrive.DefaultPath + formattedPath; // PATH specified is always relative to the desktop
    }

    // SAVE expression(string)
    IEnumerator Save()
    {
        SkipToken();

        string fileName = ExpectStringExpression();

        string formattedName = "";
        foreach (char character in fileName) formattedName += (char)(character - 224);

        _keyboard.SetCanFlashCursor(false);
        IsProgramRunning = true;

        // TODO handle reversed text and all others that are invalid as formatted

        // from start of program to end 
        byte[] programToSave = _vm.Ram[_vm.ReadWordFromRam(_vm.pProgramSpaceStart).._vm.ReadWordFromRam(_vm.pProgramSpaceEnd)];
        _vm.DiskDrive.FileLoader.SaveFileToComputer(programToSave, formattedName);

        float saveProgress = 0;

        _keyboard.PrintFormattedTextToScreen($"saving \"{formattedName}\"...");

        while (saveProgress < programToSave.Length + _vm.DiskDrive.BytesPerSecond)
        {
            saveProgress += _vm.DiskDrive.BytesPerSecond * Time.deltaTime;
            yield return null;
        }

        _keyboard.PrintFormattedTextToScreen("done");

        _keyboard.SetCanFlashCursor(true);
        IsProgramRunning = false;
    }

    // TODO load actual amount progressively and allow for breaking
    // LOAD expression(string)
    IEnumerator Load()
    {
        SkipToken();

        string fileName = ExpectStringExpression();

        string formattedName = "";
        foreach (char character in fileName) formattedName += (char)(character - 224);

        if (!File.Exists(_vm.DiskDrive.FilePath + $"\\{formattedName}.lmcprg")) // TODO handle errors elsewhere
        {
            _keyboard.PrintFormattedTextToScreen("file does not exist");
            yield break;
        }

        _keyboard.SetCanFlashCursor(false);
        IsProgramRunning = true;

        byte[] programBytes = _vm.DiskDrive.FileLoader.LoadFileFromComputer(formattedName);

        _vm.WriteByteArrayToRam(_vm.ReadWordFromRam(_vm.pProgramSpaceStart), programBytes);

        _vm.WriteWordToRam(_vm.pProgramSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceStart) + programBytes.Length); // set end of program pointer
        _vm.WriteWordToRam(_vm.pVariableSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceStart) + programBytes.Length); // set end of program pointer
        _vm.WriteWordToRam(_vm.pBoolSpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceStart) + programBytes.Length); // set end of program pointer
        _vm.WriteWordToRam(_vm.pArraySpaceEnd, _vm.ReadWordFromRam(_vm.pProgramSpaceStart) + programBytes.Length); // set end of program pointer

        float loadProgress = 0;

        _keyboard.PrintFormattedTextToScreen($"loading \"{formattedName}\"...");

        while (loadProgress < programBytes.Length + _vm.DiskDrive.BytesPerSecond) // time of program + 1 additional second // TODO may remove later
        {
            loadProgress += _vm.DiskDrive.BytesPerSecond * Time.deltaTime;
            yield return null;
        }

        _keyboard.PrintFormattedTextToScreen("done");

        _keyboard.SetCanFlashCursor(true);
        IsProgramRunning = false;
    }

    // LIST
    // TODO fix line printing, sometimes there are gaps created when scrolling
    IEnumerator ListProgram()
    {
        _breakPressed = false;
        IsProgramRunning = true;
        _keyboard.SetCanFlashCursor(false);

        CurrentLineIndex++; // move over list command
        TokensExecuted++;

        bool inQuotes = false;
        int address = _vm.ReadWordFromRam(_vm.pProgramSpaceStart); // starting address of program space

        while (_vm.ReadWordFromRam(address) != 65535) // if line pointer is not 255 terminator
        {
            string line = "";

            word lineNumber = _vm.ReadWordFromRam(address + 2);

            foreach (char character in lineNumber.ToString())
            {
                line += (char)(character + 224);
            }

            line += KeyboardAndScreenEditor.Space;

            int index = 4;

            while (_vm.Ram[address + index] != 255)
            {
                byte b = _vm.Ram[address + index];

                if (_vm.CharacterSetValues[b] == KeyboardAndScreenEditor.Quote) inQuotes = !inQuotes;

                if (b < 128 || inQuotes) line += _vm.CharacterSetValues[b]; // add screen text
                else if (!inQuotes)
                {
                    string keyword = _tokeniser.KeywordStrings[b - 128];
                    foreach (char character in keyword) line += (char)(character + 224); // convert keyword characters to screen text
                }

                index++;
            }

            _keyboard.PrintRawTextToScreen(line);

            TotalBudget = 0;

            while (TotalBudget < 1) // wait before continuing
            {
                TotalBudget += Time.deltaTime * 20; // total budget for the frame
                yield return null;
            }

            if (_breakPressed)
            {
                _breakPressed = false;

                _keyboard.CarriageReturn();
                _keyboard.PrintFormattedTextToScreen($"break");
                break;
            }

            address = _vm.ReadWordFromRam(address);
        }

        _keyboard.SetCanFlashCursor(true);
        IsProgramRunning = false;
    }

    // ----------------------------- //

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

    // -------------------------------------------------- //

    // starts the run program coroutine again after a command couroutine is finished
    void RestartProgram()
    {
        if (!IsEndOfOperation()) throw new InterpreterException("01 syntax error", "unexpected token");

        _continueAddress = _vm.ReadWordFromRam(_simple.GetAllLines()[_currentLineNumber]);
        _nextLineIndex = CurrentLineIndex + 1;
        StartCoroutine(RunProgram(_simple.GetAllLines()[_currentLineNumber])); // start program from after key
    }

    // checks if the current token is a colon ":" or an EOF token
    bool IsEndOfOperation() => CurrentLine[CurrentLineIndex].TokenType == TokenType.Colon || CurrentLine[CurrentLineIndex].TokenType == TokenType.EOF;

    bool IsTokenOfTypes(TokenType[] types)
    {
        TokenType token = CurrentLine[CurrentLineIndex].TokenType;
        for (int i = 0; i < types.Length; i++) if (token != types[i]) return false;
        return true;
    }

    float ExpectFloatExpression()
    {
        object expression = _parser.ParseExpression();
        if (expression is not float floatExpression) throw new InterpreterException("02 type mismatch error", "expected float expression");
        return floatExpression;
    }

    bool ExpectBoolExpression()
    {
        object expression = _parser.ParseExpression();
        if (expression is not bool boolExpression) throw new InterpreterException("02 type mismatch error", "expected float expression");
        return boolExpression;
    }

    string ExpectStringExpression()
    {
        object expression = _parser.ParseExpression();
        if (expression is not string stringExpression) throw new InterpreterException("02 type mismatch error", "expected float expression");
        return stringExpression;
    }

    void RunInProgramOnly(string command)
    {
        if (!IsProgramRunning) throw new InterpreterException("08 illegal direct", $"{command} cannot be executed in direct mode");
    }

    void RunInDirectOnly(string command)
    {
        if (IsProgramRunning) throw new InterpreterException("08 illegal direct", $"{command} cannot be executed inside a program");
    }

    public void SkipToken()
    {
        CurrentLineIndex++;
        TokensExecuted++;
    }

    Tokeniser.Token ExpectToken(TokenType type)
    {
        Tokeniser.Token token = CurrentLine[CurrentLineIndex];
        if (CurrentLine[CurrentLineIndex].TokenType != type) throw new InterpreterException("01 syntax error", "unexpected token");

        SkipToken();

        return token;
    }

    byte[] GetLineBytes(word address)
    {
        word currentAddress = address + 4; // skip past line number and pointer

        List<byte> line = new List<byte>();

        while (_vm.Ram[currentAddress] != 255)
        {
            line.Add(_vm.Ram[currentAddress]);
            currentAddress++;
        }

        return line.ToArray();
    }

    // calculates how long a line should take to execute. If the code finished before his time it has to wait.
    int CalculateLineTimePenalty()
    {
        int penalty = 0;

        for (int i = 0; i < TokensExecuted; i++)
        {
            Tokeniser.Token token = CurrentLine[i];

            // TODO if string identifier, get string length and add penalty
            if (token.TokenType == TokenType.Text) penalty += 1 + (int)(token.StringValue.Length * 0.2f); // time penalty based on length
            else penalty += TimePenalties[token.TokenType]; // time penalty of keyword
        }

        return penalty;
    }

    void SkipToEndOfLine() => CurrentLineIndex = CurrentLine.Length - 1;



    // ---------------------------------------- //

    // how long it takes the LMC-77 to execute each of these commands.
    public Dictionary<TokenType, int> TimePenalties = new()
    {
        // Variables & literals
        { TokenType.Identifier, 2 },
        { TokenType.Number, 1 },
        { TokenType.Text, 1 },
        { TokenType.True, 1 },
        { TokenType.False, 1 },
        { TokenType.Marker, 1 },

        { TokenType.EOF, 0 },

        // Prefix expressions (single operand)
        { TokenType.Asc, 1 },
        { TokenType.Chr, 1 },
        { TokenType.Len, 2 },
        { TokenType.Slc, 3 },
        { TokenType.Abs, 1 },
        { TokenType.Sin, 2 },
        { TokenType.Cos, 2 },
        { TokenType.Tan, 2 },
        { TokenType.Sgn, 1 },
        { TokenType.Sqr, 2 },
        { TokenType.Flr, 1 },
        { TokenType.Cel, 1 },
        { TokenType.Exp, 2 },
        { TokenType.Val, 2 },
        { TokenType.Str, 2 },
        

        // Prefix (no parenthesis)
        { TokenType.Get, 5 },
        { TokenType.Key, 3 },
        { TokenType.Parse, 3 },
        { TokenType.Time, 1 },
        { TokenType.Read, 3 },

        // Expressions (no operand)
        { TokenType.Fre, 1 },
        { TokenType.Rnd, 3 },

        // Statements
        { TokenType.If, 2 },
        { TokenType.Then, 1 },
        { TokenType.For, 2 },
        { TokenType.To, 1 },
        { TokenType.Step, 1 },
        { TokenType.Next, 2 },
        { TokenType.Not, 1 },
        { TokenType.Reset, 1 },
        { TokenType.Goto, 3 },
        { TokenType.Gosub, 5 },
        { TokenType.Return, 5 },
        { TokenType.End, 1 },
        { TokenType.Write, 2 },
        { TokenType.On, 2 },
        { TokenType.Com, 1 },
        { TokenType.Let, 2 },
        { TokenType.Save, 5 },
        { TokenType.Load, 5 },
        { TokenType.List, 5 },
        { TokenType.New, 3 },
        { TokenType.Dir, 5 },
        { TokenType.Run, 3 },
        { TokenType.Stop, 2 },
        { TokenType.Cont, 1 },
        { TokenType.Delete, 5 },
        { TokenType.Csr, 4 },
        { TokenType.Print, 2 },
        { TokenType.Clear, 2 },
        { TokenType.Path, 5 },
        { TokenType.Renumber, 5 },
        { TokenType.Comma, 1 },
        { TokenType.Colon, 1 },
        { TokenType.OpenBracket, 1 },
        { TokenType.CloseBracket, 1 },
        { TokenType.OpenSquareBracket, 1 },
        { TokenType.CloseSquareBracket, 1 },
        { TokenType.Dim, 3 },
        { TokenType.Query, 3 },
        { TokenType.Wait, 0 }, // dont want wait to interfere with timing



        // Type identifiers
        { TokenType.Percent, 0},
        { TokenType.Dollar, 0 },
        { TokenType.At, 0 },

        // Operators
        { TokenType.Plus, 1 },
        { TokenType.Minus, 1 },
        { TokenType.Multiply, 2 },
        { TokenType.Divide, 2 },
        { TokenType.Equals, 3 },
        { TokenType.Power, 5 },

        // Comparison operators
        { TokenType.Or, 2 },
        { TokenType.And, 3 },
        { TokenType.EqualTo, 3 },
        { TokenType.NotEqualTo, 3 },
        { TokenType.Less, 2 },
        { TokenType.Greater, 2 },
        { TokenType.LessEqual, 2 },
        { TokenType.GreaterEqual, 2 }
    };
}

// exception to be thrown if an error occurs in the simple program
public class InterpreterException : Exception
{
    public string ErrorType;

    public InterpreterException(string errorType, string message)
    : base(message)
    {
        ErrorType = errorType;
    }
}
