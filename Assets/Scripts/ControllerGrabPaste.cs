using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Valve.VR;

public class ControllerGrabPaste : MonoBehaviour
{
    public SteamVR_Action_Boolean m_GrabAction = null;
    public SteamVR_Action_Single squeezeAction;

    public bool isLeft = true;

    private SteamVR_Behaviour_Pose m_Pose = null;
    private FixedJoint m_Joint = null;

    public float triggerValue;
    private Interactable m_CurrentInteractable = null;
    public List<Interactable> m_ContactInteractables = new List<Interactable>();

    public float DistMin;

    private void Awake()
    {
        m_Pose = GetComponent<SteamVR_Behaviour_Pose>();
        m_Joint = GetComponent<FixedJoint>();
    }

    void Update()
    {
        if (isLeft)
        {
            triggerValue = squeezeAction.GetAxis(SteamVR_Input_Sources.LeftHand);
        }
        else
        {
            triggerValue = squeezeAction.GetAxis(SteamVR_Input_Sources.RightHand);
        }

        if (m_GrabAction.GetStateDown(m_Pose.inputSource))
        {
            Pickup();
        }

        if (m_GrabAction.GetStateUp(m_Pose.inputSource))
        {
            Drop();
        }

    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.gameObject.CompareTag("Interactable"))
            return;

        m_ContactInteractables.Add(other.gameObject.GetComponent<Interactable>());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.gameObject.CompareTag("Interactable"))
            return;

        m_ContactInteractables.Clear();

    }

    private void Pickup()
    {
        m_CurrentInteractable = GetNearestInteractable();

        if (!m_CurrentInteractable)
            return;

        if (m_CurrentInteractable.m_ActiveControllerPaste)
            m_CurrentInteractable.m_ActiveControllerPaste.Drop();

        m_CurrentInteractable.transform.position = transform.Find("Sphere").transform.position;

        Rigidbody targetBody = m_CurrentInteractable.GetComponent<Rigidbody>();
        m_CurrentInteractable.GetComponent<BoxCollider>().enabled = false;
        m_Joint.connectedBody = targetBody;

        m_CurrentInteractable.m_ActiveControllerPaste = this;
    }

    private void Drop()
    {
        if (!m_CurrentInteractable)
            return;

        Rigidbody targetbody = m_CurrentInteractable.GetComponent<Rigidbody>();
        m_CurrentInteractable.GetComponent<BoxCollider>().enabled = true;
        targetbody.velocity = m_Pose.GetVelocity();
        targetbody.angularVelocity = m_Pose.GetAngularVelocity();

        GetCubeNearestInteractable(m_CurrentInteractable);

        m_Joint.connectedBody = null;

        m_CurrentInteractable.m_ActiveControllerPaste = null;
        m_CurrentInteractable = null;
    }

    private Interactable GetNearestInteractable()
    {
        Interactable nearest = null;
        float minDistance = float.MaxValue;
        float distance = 0.0f;

        foreach (Interactable interactable in m_ContactInteractables)
        {
            distance = (interactable.transform.position - GetComponent<Collider>().transform.position).sqrMagnitude;

            if (distance < minDistance)
            {
                minDistance = distance;
                nearest = interactable;
            }
        }
        return nearest;
    }

    private void GetCubeNearestInteractable(Interactable cube)
    {
        Interactable nearest = null;
        float minDistance = DistMin;
        float distance = 0.0f;

        foreach (Interactable interactable in m_ContactInteractables)
        {
            if (interactable != cube)
            {
                distance = (interactable.transform.position - GetComponent<Collider>().transform.position).sqrMagnitude;

                if (distance < minDistance)
                {
                    minDistance = distance;
                    nearest = interactable;
                }
            }
        }
        if (nearest != null)
        { 
            if (cube.gameObject.GetComponent<FixedJoint>() == null)
            {
                cube.gameObject.AddComponent<FixedJoint>();
                cube.GetComponent<FixedJoint>().breakForce = 10000;
            }
            cube.GetComponent<FixedJoint>().connectedBody = nearest.GetComponent<Rigidbody>();
        }
    }
}