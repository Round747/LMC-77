using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class LMCInputManager : MonoBehaviour
{
    public static LMCInputManager Instance;
    public InputActionAsset Asset;

    [SerializeField] KeyboardAndScreenEditor _keyboardHandler;
    [SerializeField] Interpreter _interpreter;
    [SerializeField] VirtualMachine _vm;

    int controlKeysHeld = 0;

    // Cursor actions
    public static Action<bool> CursorLeft;
    public static Action<bool> CursorUp;
    public static Action<bool> CursorDown;
    public static Action<bool> CursorRight;

    public static Action<char> TextPressed;
    public static Action BreakPressed;

    public static Action Screenshot;

    public static Action KeyBoardDown;

    public void Awake()
    {
        Instance = this;

        Cursor.visible = false;
    }

    public void OnEnable()
    {
        //* input keys, including text input
        Keyboard.current.onTextInput += TextInputPressed;
        Asset.FindActionMap("LMC").FindAction("Escape").performed += BreakPerformed;
        Asset.FindActionMap("LMC").FindAction("Cursor").performed += CursorMovement;
        Asset.FindActionMap("LMC").FindAction("Cursor").canceled += CancelCursorMovement;

        //* Modifier keys
        Asset.FindActionMap("LMC").FindAction("Control").performed += ControlPerformed;
        Asset.FindActionMap("LMC").FindAction("Control").canceled += ControlCanceled;

        Asset.FindActionMap("LMC").FindAction("Screenshot").performed += ScreenShotPerformed;

    }

    public void OnDisable()
    {
        //* input keys, including text input
        Keyboard.current.onTextInput += TextInputPressed;
        Asset.FindActionMap("LMC").FindAction("Escape").performed -= BreakPerformed;
        Asset.FindActionMap("LMC").FindAction("Cursor").performed -= CursorMovement;
        Asset.FindActionMap("LMC").FindAction("Cursor").canceled -= CancelCursorMovement;

        //* Modifier keys
        Asset.FindActionMap("LMC").FindAction("Control").performed -= ControlPerformed;
        Asset.FindActionMap("LMC").FindAction("Control").canceled -= ControlCanceled;
    }

    float _initialDelay = 0.30f;
    float _repeatDelay = 0.05f;
    Action<bool> _actionToInvoke;

    void ControlPerformed(InputAction.CallbackContext _)
    {
        if (_interpreter.IsProgramRunning) return; // inputs disabled when running program

        controlKeysHeld++;
        _keyboardHandler.ControlHeld = true;
    }

    void ControlCanceled(InputAction.CallbackContext _)
    {
        controlKeysHeld--;
        _keyboardHandler.ControlHeld = controlKeysHeld > 0; // if no control keys are held, control is off, otherwise its on
    }


    void TextInputPressed(char key)
    {
        // includes all keys except shift and caps lock

        if (_interpreter.IsProgramRunning) return; // inputs disabled when running program

        TextPressed?.Invoke(key);
    }

    void BreakPerformed(InputAction.CallbackContext _)
    {
        BreakPressed?.Invoke();
    }

    void CursorMovement(InputAction.CallbackContext e)
    {
        if (_interpreter.IsProgramRunning) return; // inputs disabled when running program

        Vector2 vector = e.action.ReadValue<Vector2>();

        switch (vector.x)
        {
            case 1:
                _actionToInvoke = CursorRight;
                break;
            case -1:
                _actionToInvoke = CursorLeft;
                break;
            default:
                break;
        }

        switch (vector.y)
        {
            case 1:
                _actionToInvoke = CursorUp;
                break;
            case -1:
                _actionToInvoke = CursorDown;
                break;
            default:
                break;
        }

        // if first key press, invoke and then start repeating, otherwise just change the direction the repeat will be moving in
        if (!IsInvoking(nameof(RepeatCursorMovement)))
        {
            _actionToInvoke?.Invoke(true);
            InvokeRepeating(nameof(RepeatCursorMovement), _initialDelay, _repeatDelay);
        }
    }

    void RepeatCursorMovement() => _actionToInvoke?.Invoke(true);

    void CancelCursorMovement(InputAction.CallbackContext e)
    {
        CancelInvoke(nameof(RepeatCursorMovement));
    }

    void ScreenShotPerformed(InputAction.CallbackContext _)
    {
        Screenshot.Invoke();
    }

    // all valid keys for the LMC Keyboard. The index of the keycode is the value of the key code and what is used in the input buffer and key byte
    public readonly KeyCode[] InputKeyCodes =
    {
        KeyCode.None, // 0 byte represents no key pressed, in array as keycode none as that is skipped in input checking

        // Letters
        KeyCode.A, KeyCode.B, KeyCode.C, KeyCode.D, KeyCode.E,
        KeyCode.F, KeyCode.G, KeyCode.H, KeyCode.I, KeyCode.J,
        KeyCode.K, KeyCode.L, KeyCode.M, KeyCode.N, KeyCode.O,
        KeyCode.P, KeyCode.Q, KeyCode.R, KeyCode.S, KeyCode.T,
        KeyCode.U, KeyCode.V, KeyCode.W, KeyCode.X, KeyCode.Y,
        KeyCode.Z,

        // Number row
        KeyCode.Alpha0, KeyCode.Alpha1, KeyCode.Alpha2,
        KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
        KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8,
        KeyCode.Alpha9,

        // Punctuation / symbols
        KeyCode.BackQuote,
        KeyCode.Minus,
        KeyCode.Equals,
        KeyCode.LeftBracket,
        KeyCode.RightBracket,
        KeyCode.Backslash,
        KeyCode.Semicolon,
        KeyCode.Quote,
        KeyCode.Comma,
        KeyCode.Period,
        KeyCode.Slash,

        // Whitespace / editing
        KeyCode.Space,
        KeyCode.Return,
        KeyCode.Backspace,
        KeyCode.Tab,
        KeyCode.Escape,

        // Navigation
        KeyCode.UpArrow,
        KeyCode.DownArrow,
        KeyCode.LeftArrow,
        KeyCode.RightArrow,

        // Modifiers
        KeyCode.LeftShift,
        KeyCode.RightShift,

        // Lock keys
        KeyCode.CapsLock,
    };

    List<KeyCode> _keysHeld = new List<KeyCode>();

    void OnGUI()
    {
        if (!Event.current.isKey) return;

        if (Event.current.type == EventType.KeyDown)
        {
            KeyCode key = Event.current.keyCode;
            if (key == KeyCode.None || !InputKeyCodes.Contains(key)) return; // key is not part of the LMC keyboard

            KeyBoardDown?.Invoke();

            _vm.WriteByteToRam(_vm.LastKeyDown, (byte)Array.IndexOf(InputKeyCodes, key)); // if the key pressed is part of the LMC keyboard, store it in ram

            if (!_keysHeld.Contains(key)) _keysHeld.Add(key); // method is called every frame, so only add to list once
            _vm.WriteByteToRam(_vm.KeysHeld, (byte)_keysHeld.Count); // update keys held count
            _vm.WriteByteToRam(_vm.KeyPressed, (byte)Array.IndexOf(InputKeyCodes, key)); // update current key pressed

        }
        else if (Event.current.type == EventType.KeyUp)
        {
            KeyCode key = Event.current.keyCode;
            if (key == KeyCode.None || !InputKeyCodes.Contains(key)) return; // key is not part of the LMC keyboard

            _vm.WriteByteToRam(_vm.LastKeyUp, (byte)Array.IndexOf(InputKeyCodes, key)); // if the key pressed is part of the LMC keyboard, store it in ram

            _keysHeld.Remove(key); // key is no longer being held
            _vm.WriteByteToRam(_vm.KeysHeld, (byte)_keysHeld.Count); // update keys held count

            if ((byte)_keysHeld.Count == 0) _vm.WriteByteToRam(_vm.KeyPressed, 0); // no key held, byte value of 0
            else if (_vm.Ram[_vm.KeyPressed] == (byte)Array.IndexOf(InputKeyCodes, key)) // if the key released was the key currently being pressed
            {
                _vm.WriteByteToRam(_vm.KeyPressed, (byte)Array.IndexOf(InputKeyCodes, _keysHeld[^1])); // the new key being pressed was the last key held before the one released
            }
        }

    }
}
