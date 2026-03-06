using System.Collections;
using System.Threading;
using Unity.VisualScripting;
using UnityEngine;

public class Test : MonoBehaviour
{

    // char _textKey;
    // bool _textKeyPressed = false;

    // IEnumerator Key()
    // {
    //     RunInProgramOnly("key");

    //     SkipToken(); // skip key

    //     Tokeniser.Token variable = ExpectToken(TokenType.Identifier);

    //     Parser.Atom identifier = new Parser.Atom(variable); // create new atom from variable

    //     // TODO handle array variables

    //     if (identifier.GetDataType() == '$') // expect a text key
    //     {
    //         _textKeyPressed = false;

    //         Keyboard.current.onTextInput += WaitForTextKey;

    //         while (!_textKeyPressed)  // wait until a key is pressed, this will set the _textKey variable
    //         {
    //             if (_breakPressed) // the break key is only checked at the end of every line while the program is running. Check here since the coroutine hasnt started yet
    //             {
    //                 _keyboard.CarriageReturn();
    //                 IsProgramRunning = false;
    //                 _keyboard.SetCanFlashCursor(true);
    //                 Keyboard.current.onTextInput -= WaitForTextKey;

    //                 yield break;
    //             }
    //             yield return null;
    //         }

    //         // yield return new WaitWhile(() => !_textKeyPressed);

    //         Keyboard.current.onTextInput -= WaitForTextKey;

    //         // TODO handle array variables
    //         _simple.SetStringVariable(identifier.GetVariableName(), _textKey.ToString());

    //         // _simple.DeleteAllVariables(); // all variables are wiped before running, but not after. CONT will not wipe variables hence why this method is placed here and not in the method
    //         // _vm.WriteByteToRam(_vm.StackPointer, 255); // same reasoning with resetting stack pointer
    //         _continueAddress = _vm.ReadWordFromRam(_simple.GetAllLines()[_currentLineNumber]); // reset cont, address not used
    //         _nextLineIndex = CurrentLineIndex;
    //         StartCoroutine(RunProgram(_simple.GetAllLines()[_currentLineNumber])); // start program at beginning

    //         yield break;
    //     }
    //     else if (identifier.GetDataType() == '?') throw new InterpreterException("02 type mismatch error", "invalid datatype");


    //     // StartCoroutine(RunProgram())
    // }

    // void WaitForTextKey(char key)
    // {
    //     if (char.IsControl(key) || key.Equals(' ')) return;

    //     _textKey = (char)(key + 224); // shift up to screen text
    //     print($"KEY PRESSED: {key}");
    //     _textKeyPressed = true;
    // }
}
