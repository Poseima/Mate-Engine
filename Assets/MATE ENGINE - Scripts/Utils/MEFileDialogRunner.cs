using System;
using System.Collections.Generic;
using UnityEngine;

// Unity main-thread dispatcher used by MEFileDialog to marshal native dialog callbacks back onto Update().
public sealed class MEFileDialogRunner : MonoBehaviour
{
    private static readonly object Gate = new object();
    private static readonly Queue<Action> Queue = new Queue<Action>();

    public static void Post(Action a)
    {
        if (a == null) return;
        lock (Gate) Queue.Enqueue(a);
    }

    private void Update()
    {
        while (true)
        {
            Action a;
            lock (Gate)
            {
                if (Queue.Count == 0) return;
                a = Queue.Dequeue();
            }

            try { a(); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }
}

