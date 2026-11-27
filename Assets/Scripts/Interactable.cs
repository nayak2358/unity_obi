// Keeps track of which controller holds a cube

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class Interactable : MonoBehaviour
{
    [HideInInspector]
    public ControllerGrab m_ActiveController = null;
    [HideInInspector]
    public ControllerGrabPaste m_ActiveControllerPaste = null;
}
