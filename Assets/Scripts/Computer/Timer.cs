using System;
using Types;
using UnityEngine;

public class Timer : MonoBehaviour
{
    [SerializeField] VirtualMachine _vm;

    float _tickTime = 1f / 60f;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        StartTimer();
    }

    public void StartTimer()
    {
        CancelInvoke(nameof(TickTimer));
        _vm.WriteWordToRam(_vm.Timer, 0); // clear timer
        InvokeRepeating(nameof(TickTimer), _tickTime, _tickTime); // ~60 times per second
    }

    void TickTimer()
    {
        _vm.WriteWordToRam(_vm.Timer, _vm.ReadWordFromRam(_vm.Timer) + 1); // increment timer
    }
}
