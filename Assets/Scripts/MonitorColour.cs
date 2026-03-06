using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MonitorColour : MonoBehaviour
{
    [SerializeField] TMP_Text _textField;
    [SerializeField] Image _background;
    [SerializeField] Image _CursorMask;
    [SerializeField] Image _CursorBackground;

    [Header("Colours")]
    [SerializeField] Colours _selectedPalette;
    [SerializeField] Color[] _textColours;
    [SerializeField] Color[] _backgroundColours;


    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        Initialise();
    }

    public void Initialise()
    {
        _textField.color = _textColours[(int)_selectedPalette];
        _background.color = _backgroundColours[(int)_selectedPalette];
        _CursorBackground.color = _textColours[(int)_selectedPalette];
        _CursorMask.color = _backgroundColours[(int)_selectedPalette];
    }

    [Serializable]
    public enum Colours
    {
        Green,
        Amber,
        White,
        Blue
    }
}
