using UnityEngine;
using AbsractSyntaxTree;
using System;
using Types;
using System.Collections.Generic;

public class Parser : MonoBehaviour
{
    [SerializeField] Interpreter _interpreter;
    [SerializeField] VirtualMachine _vm;
    [SerializeField] Tokeniser _tokeniser;

    // ---------------------------------- //

    // I wrote this parser in another project a few months ago, the recursion makes it really difficult to understand so the code may not be perfect
    // I have essentially copied it and swapped out names and cleaned it up where I could, I haven't added many comments because I don't fully understand what it's doing

    IExpression _lhs; // left hand side

    // TODO make sure the end of expression is the correct one, if opening parenthesis, expect closing, not any eoe token

    public object ParseExpression() // TODO handle multiple expressions seperated by commans and make sure that there is only one when one is expected
    {
        CheckParenthesis();

        // _tokenIndex = startIndex;

        IExpression parse = RecursiveParse(0);

        output = "";
        DebugParse(0, parse); // TODO remove this
        print(output);

        return parse.EvaluateExpression(_vm); // vm reference needs to be parsed so that the result can write to and read from ram
    }

    IExpression RecursiveParse(float precedent)
    {
        print("parse starting at " + _interpreter.CurrentLineIndex);
        _lhs = null;

        if (IsEndOfExpression(PeekToken())) throw new InterpreterException("01 syntax error", "expression expected");

        NullDenotation();

        while (true)
        {
            Tokeniser.Token nextToken = PeekToken();
            print("next token: " + nextToken.TokenType);

            if (nextToken.TokenType == Tokeniser.TokenType.CloseBracket || // should stop at closing bracket
            IsEndOfExpression(nextToken) || // last atom, no operator
            GetBindingPower(nextToken).x < precedent) break; // left binding power

            _lhs = LeftDenotation(ExpectInfixOperator(), _lhs);
        }

        print($"ending parse ar token index {_interpreter.CurrentLineIndex}");
        return _lhs;
    }

    void NullDenotation()
    {
        Tokeniser.Token token = GetToken();
        print("Token is " + token.TokenType);

        if (IsPrefixOperator(token))
        {
            if (IsPrefixMethod(token) && PeekToken().TokenType != Tokeniser.TokenType.OpenBracket) throw new InterpreterException("01 syntax error", $"parenthesis expected for method {_tokeniser.KeywordStrings[(int)(token.TokenType - 128)]}");

            if (token.TokenType == Tokeniser.TokenType.Fre || token.TokenType == Tokeniser.TokenType.Rnd) // these methods dont expect an expression as an operand
            {
                _ = GetToken(); // discard opening bracket

                Tokeniser.Token newToken = GetToken();

                if (newToken.TokenType != Tokeniser.TokenType.CloseBracket) throw new InterpreterException("01 syntax error", $"{_tokeniser.KeywordStrings[(int)(token.TokenType - 128)]} does not take any arguments");

                _lhs = new PrefixOperator(token, null); // no expression as this prefix method wont use it
                return;
            }
            else if (token.TokenType == Tokeniser.TokenType.Slc)
            {
                print("parsing first argument");
                if (GetToken().TokenType != Tokeniser.TokenType.OpenBracket) throw new InterpreterException("01 syntax error", $"parenthesis expected for method {_tokeniser.KeywordStrings[(int)(token.TokenType - 128)]}");

                IExpression argument1 = RecursiveParse(0);
                if (GetToken().TokenType != Tokeniser.TokenType.Comma) throw new InterpreterException("01 syntax error", "unexpected token");
                IExpression argument2 = RecursiveParse(0);
                if (GetToken().TokenType != Tokeniser.TokenType.Comma) throw new InterpreterException("01 syntax error", "unexpected token");
                IExpression argument3 = RecursiveParse(0);
                if (GetToken().TokenType != Tokeniser.TokenType.CloseBracket) throw new InterpreterException("01 syntax error", "closing parenthesis expected");

                // custome IExpression because Slc expects three arguments
                _lhs = new Slc(argument1, argument2, argument3);
                return;
            }

            _lhs = new PrefixOperator(token, RecursiveParse(10)); // precedent of 10 so it only grabs the first atom, or whole expression if in parenthesis
            return;
        }
        else if (token.TokenType == Tokeniser.TokenType.OpenBracket)
        {
            print("recursing at open bracket");
            _lhs = RecursiveParse(0); // precedence of 0 to parse everything enclosed in the brackets
            _ = GetToken(); // discard closing bracket
            return;
        }
        else if (IsEndOfExpression(token)) return; // loop after nud will end parse
        else if (!IsAtom(token)) throw new InterpreterException("01 syntax error", "atom expected"); // expression should not start with an operator
        else if (token.TokenType == Tokeniser.TokenType.Identifier && PeekToken().TokenType == Tokeniser.TokenType.OpenSquareBracket) // array variable instead of a normal variable
        {
            GetToken(); // skip over bracket
            List<IExpression> indexes = new List<IExpression>();

            while (PeekToken().TokenType != Tokeniser.TokenType.CloseSquareBracket)
            {
                IExpression next = RecursiveParse(0);

                Tokeniser.Token nextToken = PeekToken();

                if (nextToken.TokenType != Tokeniser.TokenType.Comma && nextToken.TokenType != Tokeniser.TokenType.CloseSquareBracket) throw new InterpreterException("01 syntax error", "unexpected token");

                if (nextToken.TokenType == Tokeniser.TokenType.Comma) _ = GetToken();

                indexes.Add(next);
            }

            _ = GetToken(); // skip end bracket

            _lhs = new ArrayAtom(token, indexes.ToArray()); // create array with list of indexes and identifier token for name
            return;
        }

        _lhs = new Atom(token);
    }

    InfixOperator LeftDenotation(Tokeniser.Token infixOperator, IExpression lhs)
    {
        IExpression rhs = RecursiveParse(GetBindingPower(infixOperator).y); // recurse with right binding power
        return new InfixOperator(infixOperator, lhs, rhs);
    }

    // returns the next token and increases the token index
    Tokeniser.Token GetToken()
    {
        Tokeniser.Token token = _interpreter.CurrentLine[_interpreter.CurrentLineIndex];
        _interpreter.CurrentLineIndex++;
        _interpreter.TokensExecuted++;
        return token;
    }

    // returns the next token without increasing the index
    Tokeniser.Token PeekToken()
    {
        int peekIndex = _interpreter.CurrentLineIndex;
        // int peekIndex = _tokenIndex + 1;

        if (peekIndex < _interpreter.CurrentLine.Length) return _interpreter.CurrentLine[peekIndex];
        else return new Tokeniser.Token("", Tokeniser.TokenType.EOF);
    }

    Tokeniser.Token ExpectInfixOperator()
    {
        Tokeniser.Token token = GetToken();

        if (!IsInfixOperator(token)) throw new InterpreterException("01 syntax error", "operator expected");
        else return token;
    }

    // return two expressions seperated by commas i.e. "a, 'e' + 2"
    // does not expect parenthesis (used for commands not methods)
    public (object, object) ParseTwoExpressions()
    {
        CheckParenthesis();

        // _tokenIndex = startIndex;

        IExpression argument1 = RecursiveParse(0);

        if (GetToken().TokenType != Tokeniser.TokenType.Comma) throw new InterpreterException("01 syntax error", "unexpected token");

        IExpression argument2 = RecursiveParse(0);

        return (argument1.EvaluateExpression(_vm), argument2.EvaluateExpression(_vm));

    }

    void CheckParenthesis()
    {
        int open = 0;
        int close = 0;

        for (int i = 0; _interpreter.CurrentLineIndex + i < _interpreter.CurrentLine.Length; i++)
        {
            if (_interpreter.CurrentLine[_interpreter.CurrentLineIndex + i].TokenType == Tokeniser.TokenType.OpenBracket) open++;
            else if (_interpreter.CurrentLine[_interpreter.CurrentLineIndex + i].TokenType == Tokeniser.TokenType.CloseBracket) close++;

            // print(_interpreter.CurrentLine[_interpreter.CurrentLineIndex + i].TokenType); // TODO remove

            if (_interpreter.CurrentLine[_interpreter.CurrentLineIndex + i].TokenType == Tokeniser.TokenType.EOF) break;
        }

        if (open > close) throw new InterpreterException("01 syntax error", "closing parenthesis expected");
        else if (close > open) throw new InterpreterException("01 syntax error", "opening parenthesis expected");

    }

    // methods that expect a single operand expression, like Abs() or even Slc$(), parenthesis must be there
    bool IsPrefixMethod(Tokeniser.Token token)
    {
        return token.TokenType switch
        {
            // require an expression but it is never used
            Tokeniser.TokenType.Fre => true,
            Tokeniser.TokenType.Rnd => true,

            // use expression
            Tokeniser.TokenType.Asc => true,
            Tokeniser.TokenType.Chr => true,
            Tokeniser.TokenType.Len => true,
            Tokeniser.TokenType.Slc => true,
            Tokeniser.TokenType.Abs => true,
            Tokeniser.TokenType.Sin => true,
            Tokeniser.TokenType.Cos => true,
            Tokeniser.TokenType.Tan => true,
            Tokeniser.TokenType.Sgn => true,
            Tokeniser.TokenType.Sqr => true,
            Tokeniser.TokenType.Flr => true,
            Tokeniser.TokenType.Cel => true,
            Tokeniser.TokenType.Exp => true,
            Tokeniser.TokenType.Str => true,
            Tokeniser.TokenType.Val => true,

            Tokeniser.TokenType.Read => true,
            _ => false
        };
    }

    // X is left binding power, Y is right binding power
    Vector2 GetBindingPower(Tokeniser.Token token)
    {
        return token.TokenType switch
        {
            Tokeniser.TokenType.Equals => new Vector2(1.1f, 1f),
            Tokeniser.TokenType.Or => new Vector2(2, 2.1f),
            Tokeniser.TokenType.And => new Vector2(3, 3.1f),

            Tokeniser.TokenType.EqualTo or
            Tokeniser.TokenType.NotEqualTo => new Vector2(4, 4.1f),

            Tokeniser.TokenType.Greater or
            Tokeniser.TokenType.GreaterEqual or
            Tokeniser.TokenType.Less or
            Tokeniser.TokenType.LessEqual => new Vector2(5, 5.1f),

            Tokeniser.TokenType.Plus or
            Tokeniser.TokenType.Minus => new Vector2(6, 6.1f),

            Tokeniser.TokenType.Multiply or
            Tokeniser.TokenType.Divide => new Vector2(7, 7.1f),

            Tokeniser.TokenType.Power => new Vector2(8.1f, 8),

            _ => new Vector2(0, 0),
        };
    }

    // expression can end before it encounters an eof token, signifies to stop parsing
    bool IsEndOfExpression(Tokeniser.Token token)
    {
        return token.TokenType switch
        {
            Tokeniser.TokenType.Colon => true,
            Tokeniser.TokenType.Then => true,
            Tokeniser.TokenType.To => true,
            Tokeniser.TokenType.Step => true,
            Tokeniser.TokenType.Comma => true,
            Tokeniser.TokenType.EOF => true,
            Tokeniser.TokenType.Equals => true,
            Tokeniser.TokenType.CloseSquareBracket => true,
            _ => false
        };
    }

    // TODO assignment is not handled in expressions, should the assignment = be an operator?
    bool IsInfixOperator(Tokeniser.Token token)
    {
        return token.TokenType switch
        {
            // Tokeniser.TokenType.Equals => true,
            Tokeniser.TokenType.Or => true,
            Tokeniser.TokenType.And => true,
            Tokeniser.TokenType.EqualTo => true,
            Tokeniser.TokenType.NotEqualTo => true,
            Tokeniser.TokenType.Greater => true,
            Tokeniser.TokenType.GreaterEqual => true,
            Tokeniser.TokenType.Less => true,
            Tokeniser.TokenType.LessEqual => true,
            Tokeniser.TokenType.Plus => true,
            Tokeniser.TokenType.Minus => true,
            Tokeniser.TokenType.Multiply => true,
            Tokeniser.TokenType.Divide => true,
            Tokeniser.TokenType.Power => true,
            _ => false
        };
    }

    bool IsPrefixOperator(Tokeniser.Token token)
    {
        return token.TokenType switch
        {
            Tokeniser.TokenType.Plus => true,
            Tokeniser.TokenType.Minus => true,
            Tokeniser.TokenType.Not => true,
            _ => false
        } | IsPrefixMethod(token); //prefx methods are prefixes as well
    }

    bool IsAtom(Tokeniser.Token token)
    {
        return token.TokenType switch
        {
            Tokeniser.TokenType.Identifier => true,
            Tokeniser.TokenType.Number => true,
            Tokeniser.TokenType.Text => true,
            Tokeniser.TokenType.True => true,
            Tokeniser.TokenType.False => true,
            Tokeniser.TokenType.Time => true,
            _ => false
        };
    }

    // ----------------------------------------------- //

    // Abstract syntax tree structs

    //! ints are always represented (cast) as floats when returning from EvaluateExpression
    // funnily enough this is what the c64 does as well, every int is converted to a float for math operations

    public struct Atom : IExpression
    {
        Tokeniser.Token _token;
        public Tokeniser.Token Token { get => _token; set => _token = value; }

        public Atom(Tokeniser.Token token)
        {
            _token = token;
        }

        public object EvaluateExpression(VirtualMachine vm)
        {
            // fetch value from ram if it exists, else create variable
            if (_token.TokenType == Tokeniser.TokenType.Identifier)
            {
                print("evaluating variable");

                string variableName = GetVariableName();
                char type = GetDataType();

                print($"{variableName.Length}, {type}");

                if (type.Equals('$')) return vm.Simple.GetStringVariable(variableName);
                else if (type.Equals('%')) return vm.Simple.GetIntVariable(variableName);
                else if (type.Equals('?')) return vm.Simple.GetBoolVariable(variableName);
                else return vm.Simple.GetFloatVariable(variableName); // else no type (char is character from variable name)
            }
            else if (_token.TokenType == Tokeniser.TokenType.Time) return (float)vm.ReadWordFromRam(vm.Timer);

            // literals will always be floats, not ints
            if (float.TryParse(_token.StringValue, out float number)) return number;
            else if (_token.TokenType == Tokeniser.TokenType.True) return true;
            else if (_token.TokenType == Tokeniser.TokenType.False) return false;
            else return _token.StringValue; // else is a string
        }

        public char GetDataType() => (char)(_token.StringValue[^1] - 224); // last character, shifted down to formatted text

        public string GetVariableName()
        {
            string variableName = "";

            for (int i = 0; i < 2 && i < _token.StringValue.Length; i++)
            {
                // create variable name, might be 1 or 2 characters depending on length, type identifiers arent part of this string
                if (char.IsLetter((char)(_token.StringValue[i] - 224))) variableName += _token.StringValue[i];
            }

            return variableName;
        }

    }

    public struct ArrayAtom : IExpression
    {
        Tokeniser.Token _token; // token is the identifier variable name
        public Tokeniser.Token Token { get => _token; set => _token = value; }

        IExpression[] _indexes;

        public ArrayAtom(Tokeniser.Token token, IExpression[] indexes)
        {
            _token = token;
            _indexes = indexes;
        }

        public object EvaluateExpression(VirtualMachine vm)
        {
            List<word> wordIndexes = new List<word>();

            foreach (IExpression expression in _indexes)
            {
                object result = expression.EvaluateExpression(vm);

                if (result is not float fR) throw new InterpreterException("02 type mismatch error", "expected float expression");

                if (fR < 0 || fR > 65535) throw new InterpreterException("05 illegal quanitity error", "argument outside of allowable range");

                wordIndexes.Add((int)fR);
            }

            string variableName = GetVariableName();
            char type = GetDataType();

            if (type.Equals('$')) return vm.Simple.GetStringArray(variableName, wordIndexes.ToArray());
            else if (type.Equals('%')) return vm.Simple.GetIntArray(variableName, wordIndexes.ToArray());
            else if (type.Equals('?')) return vm.Simple.GetBoolArray(variableName, wordIndexes.ToArray());
            else return vm.Simple.GetFloatArray(variableName, wordIndexes.ToArray()); // else no type (char is character from variable name)
        }

        public string GetVariableName()
        {
            string variableName = "";

            for (int i = 0; i < 2 && i < _token.StringValue.Length; i++)
            {
                // create variable name, might be 1 or 2 characters depending on length, type identifiers arent part of this string
                if (char.IsLetter((char)(_token.StringValue[i] - 224))) variableName += _token.StringValue[i];
            }

            return variableName;
        }

        public char GetDataType() => (char)(_token.StringValue[^1] - 224); // last character, shifted down to formatted text

    }

    public struct InfixOperator : IExpression
    {
        Tokeniser.Token _token;
        public Tokeniser.Token Token { get => _token; set => _token = value; }

        public IExpression LeftOperand;
        public IExpression RightOperand;

        public InfixOperator(Tokeniser.Token token, IExpression leftOperand, IExpression rightOperand)
        {
            _token = token;
            LeftOperand = leftOperand;
            RightOperand = rightOperand;

            _leftOperandValue = null;
            _rightOperandValue = null;
        }

        object _leftOperandValue;
        object _rightOperandValue;

        public object EvaluateExpression(VirtualMachine vm)
        {
            _leftOperandValue = LeftOperand.EvaluateExpression(vm);
            _rightOperandValue = RightOperand.EvaluateExpression(vm);

            return _token.TokenType switch
            {
                // math operators
                Tokeniser.TokenType.Plus => Plus(),
                Tokeniser.TokenType.Minus => Minus(),
                Tokeniser.TokenType.Multiply => Multiply(),
                Tokeniser.TokenType.Divide => Divide(),
                Tokeniser.TokenType.Power => Power(),

                // equality operators (returns bool)
                Tokeniser.TokenType.EqualTo => EqualTo(),
                Tokeniser.TokenType.NotEqualTo => !EqualTo(),
                Tokeniser.TokenType.Greater => Greater(_leftOperandValue, _rightOperandValue),
                Tokeniser.TokenType.Less => Greater(_rightOperandValue, _leftOperandValue),
                Tokeniser.TokenType.LessEqual => Greater(_rightOperandValue, _leftOperandValue) || EqualTo(),
                Tokeniser.TokenType.GreaterEqual => Greater(_leftOperandValue, _rightOperandValue) || EqualTo(),

                // boolean operators
                Tokeniser.TokenType.And => And(),
                Tokeniser.TokenType.Or => Or(),
                _ => throw new Exception($"Token {_token.TokenType} should not be an infix operator"),// this should never happen since a token can only be created as a infix operator if it is one of the above types
            };
        }

        // plus could return a float or a string
        object Plus() // TODO tostring converts a number to formatted text, shift to screen text, then combine with string
        {
            // string concatenation
            if (_leftOperandValue is string || _rightOperandValue is string)
            {
                string leftOperand;
                string rightOperand;

                // differing types
                if (_leftOperandValue is not string)
                {
                    leftOperand = ConvertLiteralToString(_leftOperandValue);
                    rightOperand = _rightOperandValue.ToString();
                    return leftOperand + rightOperand;

                }
                else if (_rightOperandValue is not string)
                {
                    rightOperand = ConvertLiteralToString(_rightOperandValue);
                    leftOperand = _leftOperandValue.ToString();
                    return leftOperand + rightOperand;
                }
                else return _leftOperandValue.ToString() + _rightOperandValue.ToString(); // concatenate strings
            }
            DisallowBools("+");
            // is not bool or string, must be number (float)
            return (float)_leftOperandValue + (float)_rightOperandValue;
        }

        float Minus()
        {
            DisallowBools("-");
            DisallowStrings("-");
            return (float)_leftOperandValue - (float)_rightOperandValue;
        }

        float Multiply()
        {
            DisallowBools("*");
            DisallowStrings("*");
            return (float)_leftOperandValue * (float)_rightOperandValue;
        }

        float Divide()
        {
            DisallowBools("/");
            DisallowStrings("/");
            return (float)_leftOperandValue / (float)_rightOperandValue;
        }

        float Power()
        {
            DisallowBools("^");
            DisallowStrings("^");
            // no native power operator in c# (^ is the bitwise XOR operator)
            return Mathf.Pow((float)_leftOperandValue, (float)_rightOperandValue);
        }

        bool EqualTo()
        {
            if (_leftOperandValue is string sl && _rightOperandValue is string sr) return sl.Equals(sr);
            else if (_leftOperandValue is float fl && _rightOperandValue is float fr) return fl == fr;
            else if (_leftOperandValue is bool bl && _rightOperandValue is bool br) return bl == br;
            else throw new InterpreterException("03 invalid operation error", "cannot perform operation == on differing types");
        }

        bool Greater(object lhs, object rhs)
        {
            DisallowBools(">");
            DisallowStrings(">");
            return (float)lhs > (float)rhs;
        }

        object Or()
        {
            DisallowStrings("or");
            if (_leftOperandValue is bool l && _rightOperandValue is bool r) return l | r;
            if (_leftOperandValue is float && _rightOperandValue is float) return (float)((int)_leftOperandValue | (int)_rightOperandValue); // TODO check this, floored and ored and then recast as a float
            else throw new InterpreterException("03 invalid operation error", "cannot perform operation or on differing types");
        }

        object And()
        {
            DisallowStrings("and");
            if (_leftOperandValue is bool bl && _rightOperandValue is bool br) return bl & br;
            else if (_leftOperandValue is float fl && _rightOperandValue is float fr) return (float)((int)fl & (int)fr);
            else throw new InterpreterException("03 invalid operation error", "cannot perform operation and on differing types");
        }

        // TODO does this need to be static? nothing else is
        // takes a literal (number or bool) and returns it as screen text
        public static string ConvertLiteralToString(object literal)
        {
            print("converting literal: " + literal);

            string literalValue = literal.ToString().ToLower(); // removes the capital T and F in True and False, and the capital I in Infinity

            string value = "";
            foreach (char character in literalValue) value += (char)(character + 224);

            print("Converted to " + value);

            return value;
        }

        // throws an error if one of the operands is a certain type
        // used when performing an operation that does not allow for certain types
        void DisallowBools(string operation)
        {
            if (_leftOperandValue is bool || _rightOperandValue is bool) throw new InterpreterException("03 invalid operation error", $"cannot perform operation {operation} on type bool");
        }

        void DisallowStrings(string operation)
        {
            if (_leftOperandValue is string || _rightOperandValue is string) throw new InterpreterException("03 invalid operation error", $"cannot perform operation {operation} on type string");
        }
    }

    public struct PrefixOperator : IExpression
    {
        Tokeniser.Token _token;
        public Tokeniser.Token Token { get => _token; set => _token = value; }

        public IExpression Operand;

        public PrefixOperator(Tokeniser.Token token, IExpression operand)
        {
            _token = token;
            Operand = operand;

            _operandValue = null;
        }

        object _operandValue;

        public object EvaluateExpression(VirtualMachine vm)
        {
            if (Operand != null) _operandValue = Operand.EvaluateExpression(vm); // if its null it wont need the value anyways

            switch (_token.TokenType)
            {
                case Tokeniser.TokenType.Plus:
                    return Positive();
                case Tokeniser.TokenType.Minus:
                    return Negative();
                case Tokeniser.TokenType.Not:
                    return Not();
                case Tokeniser.TokenType.Fre:
                    vm.CalculateFreeBytes();
                    return vm.ReadWordFromRam(vm.FreeSimpleBytes);
                case Tokeniser.TokenType.Rnd:
                    return UnityEngine.Random.Range(0f, 1f); // returns random number
                case Tokeniser.TokenType.Asc:
                    return Asc(vm);
                case Tokeniser.TokenType.Chr:
                    return Chr(vm);
                case Tokeniser.TokenType.Len:
                    ExpectStringExpression();
                    return (float)_operandValue.ToString().Length;
                case Tokeniser.TokenType.Abs:
                    ExpectFloatExpression();
                    return Mathf.Abs((float)_operandValue);
                case Tokeniser.TokenType.Sin:
                    ExpectFloatExpression();
                    return Mathf.Sin((float)_operandValue);
                case Tokeniser.TokenType.Cos:
                    ExpectFloatExpression();
                    return Mathf.Cos((float)_operandValue);
                case Tokeniser.TokenType.Tan:
                    ExpectFloatExpression();
                    return Mathf.Tan((float)_operandValue);
                case Tokeniser.TokenType.Sgn:
                    ExpectFloatExpression();
                    return (float)Math.Sign((float)_operandValue);
                case Tokeniser.TokenType.Sqr:
                    ExpectFloatExpression();
                    return (float)Math.Sqrt((float)_operandValue);
                case Tokeniser.TokenType.Flr:
                    ExpectFloatExpression();
                    return Mathf.Floor((float)_operandValue);
                case Tokeniser.TokenType.Cel:
                    ExpectFloatExpression();
                    return Mathf.Ceil((float)_operandValue);
                case Tokeniser.TokenType.Exp:
                    return Mathf.Exp((float)_operandValue);
                case Tokeniser.TokenType.Str:
                    ExpectFloatExpression();
                    string strResult = "";
                    foreach (char character in _operandValue.ToString()) strResult += (char)(character + 224); // shift to screen text
                    return strResult;
                case Tokeniser.TokenType.Val: // TODO handle properly, allowing for scientific notation and allowing letters after the number
                    ExpectStringExpression();
                    string unShift = "";
                    foreach (char character in _operandValue.ToString()) unShift += (char)(character - 224); // shift to formatted text
                    if (float.TryParse(unShift, out float valResult))
                        return valResult;
                    else throw new InterpreterException("05 illegal quantity error", "conversion failed");
                case Tokeniser.TokenType.Read:
                    return Read(vm);
                default:
                    // this should never happen since a token can only be created as a prefix operator if it is one of the above types
                    throw new Exception($"Token {_token.TokenType} should not be a prefix operator");
            }
        }

        object Positive()
        {
            if (_operandValue is bool) throw new InterpreterException("03 invalid operation error", "cannot perform operation + on type bool");
            else if (_operandValue is float op) return +op; // not sure this does anything but it's here for consistency
            else throw new InterpreterException("03 invalid operation error", "cannot perform operation + on type string");
        }

        object Negative()
        {
            if (_operandValue is bool) throw new InterpreterException("03 invalid operation error", "cannot perform operation - on type bool");
            else if (_operandValue is float op) return -op;
            else throw new InterpreterException("03 invalid operation error", "cannot perform operation - on type string");
        }

        object Not()
        {
            if (_operandValue is bool op) return !op;
            else if (_operandValue is float fOp)
            {
                // if provided number can safely be converted to 16 bit signed integer
                if ((int)fOp > 32767 || (int)fOp < -32766) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

                return (float)~(short)fOp; // bitwise not.
            }
            else throw new InterpreterException("03 invalid operation error", "cannot perform operation not on type string");
        }

        //* prefix methods

        object Asc(VirtualMachine vm)
        {
            ExpectStringExpression();
            if (_operandValue.ToString().Length == 0) throw new InterpreterException("05 illegal quanity error", "empty string");
            return (float)Array.IndexOf(vm.CharacterSetValues, _operandValue.ToString()[0]);
        }

        object Chr(VirtualMachine vm)
        {
            ExpectFloatExpression();
            float floatValue = (float)_operandValue;
            if (floatValue < 0 || floatValue > 255) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");
            return vm.CharacterSetValues[(int)floatValue].ToString();
        }

        object Read(VirtualMachine vm)
        {
            ExpectFloatExpression();

            float value = (float)_operandValue;

            if (value > vm.MemorySize || value < 0) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

            return (float)vm.Ram[(int)value];
        }

        void ExpectFloatExpression()
        {
            if (_operandValue is string || _operandValue is bool) throw new InterpreterException("02 type mismatch error", "expecting float expression");
        }

        void ExpectStringExpression()
        {
            if (_operandValue is float || _operandValue is bool) throw new InterpreterException("02 type mismatch error", "expecting string expression");
        }
    }

    public struct Slc : IExpression
    {
        Tokeniser.Token _token;
        public Tokeniser.Token Token { get => _token; set => _token = value; }

        IExpression _argument1;
        IExpression _argument2;
        IExpression _argument3;

        public Slc(IExpression argument1, IExpression argument2, IExpression argument3)
        {
            _argument1 = argument1;
            _argument2 = argument2;
            _argument3 = argument3;

            _token = new Tokeniser.Token("", Tokeniser.TokenType.Slc);
        }

        public object EvaluateExpression(VirtualMachine vm)
        {
            object word = _argument1.EvaluateExpression(vm);
            object index = _argument2.EvaluateExpression(vm);
            object length = _argument3.EvaluateExpression(vm);

            if (index is not float fI || length is not float fL) throw new InterpreterException("02 type mismatch error", "expecting float expression");
            if (word is not string sW) throw new InterpreterException("02 type mismatch error", "expecting string expression");

            if (fL == 0) throw new InterpreterException("05 invalid quantity error", "length cannot be 0");

            if (fL < 0 || fI < 0) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

            if (fI > sW.Length - 1) throw new InterpreterException("05 illegal quantity error", "argument outside of allowable range");

            // grabbing substring longer than string
            if (fI + fL > sW.Length) throw new InterpreterException("05 illegal quantity error", "length exceeds string length");

            return sW.Substring((int)fI, (int)fL);
        }
    }

    string output = "";

    void DebugParse(int level, IExpression exp)
    {
        if (exp is InfixOperator op)
        {
            for (int i = 0; i < level; i++)
            {
                if (i != level - 1) output += " |   ";
                else output += " |---";
            }

            output += $"inf({op.Token.TokenType})\n";

            DebugParse(level + 1, op.LeftOperand);
            DebugParse(level + 1, op.RightOperand);
        }
        else if (exp is Atom atom)
        {
            for (int i = 0; i < level; i++)
            {
                if (i != level - 1) output += " |   ";
                else output += " |---";
            }
            output += $"({atom.Token.TokenType})\n";
        }
        else if (exp is PrefixOperator pre)
        {
            for (int i = 0; i < level; i++)
            {
                if (i != level - 1) output += " |   ";
                else output += " |---";
            }

            output += $"pre({pre.Token.TokenType})\n";

            DebugParse(level + 1, pre.Operand);
        }
    }
}

namespace AbsractSyntaxTree
{
    public interface IExpression
    {
        public Tokeniser.Token Token { get; set; }
        public object EvaluateExpression(VirtualMachine vm);
    }
}
