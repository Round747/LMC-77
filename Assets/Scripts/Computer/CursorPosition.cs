using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CursorPosition : MonoBehaviour
{
    [SerializeField] VirtualMachine VM;

    [SerializeField] Image cursorMask;
    [SerializeField] Image cursorBackground;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        // StartCoroutine(FlashCursor(true));
    }

    public void DisableCursor()
    {
        StopAllCoroutines();
        cursorMask.color = new Color(cursorMask.color.r, cursorMask.color.g, cursorMask.color.b, 0);
        cursorBackground.color = new Color(cursorBackground.color.r, cursorBackground.color.g, cursorBackground.color.b, 0);

        CancelInvoke(nameof(FlashCursor));
    }

    public void EnableCursor()
    {
        RectTransform mask = cursorMask.GetComponent<RectTransform>();
        mask.anchoredPosition = new Vector2(VM.Ram[VM.CursorPositionX] * 8, -(VM.Ram[VM.CursorPositionY] * 8));

        RectTransform background = cursorBackground.GetComponent<RectTransform>();
        background.anchoredPosition = new Vector2(VM.Ram[VM.CursorPositionX] * 8, -(VM.Ram[VM.CursorPositionY] * 8));

        if (!IsInvoking(nameof(FlashCursor))) InvokeRepeating(nameof(FlashCursor), 0, 0.5f);
    }

    public void UpdateCursorPosition()
    {
        RectTransform mask = cursorMask.GetComponent<RectTransform>();
        mask.anchoredPosition = new Vector2(VM.Ram[VM.CursorPositionX] * 8, -(VM.Ram[VM.CursorPositionY] * 8));

        RectTransform background = cursorBackground.GetComponent<RectTransform>();
        background.anchoredPosition = new Vector2(VM.Ram[VM.CursorPositionX] * 8, -(VM.Ram[VM.CursorPositionY] * 8));

        cursorMask.color = new Color(cursorMask.color.r, cursorMask.color.g, cursorMask.color.b, 0);
        cursorBackground.color = new Color(cursorBackground.color.r, cursorBackground.color.g, cursorBackground.color.b, 0);

        CancelInvoke(nameof(FlashCursor));
        InvokeRepeating(nameof(FlashCursor), 0.035f, 0.5f);
    }

    public void FlashCursor()
    {
        cursorMask.color = new Color(cursorMask.color.r, cursorMask.color.g, cursorMask.color.b, cursorMask.color.a == 0 ? 1 : 0);
        cursorBackground.color = new Color(cursorBackground.color.r, cursorBackground.color.g, cursorBackground.color.b, cursorBackground.color.a == 0 ? 1 : 0);
    }
}
