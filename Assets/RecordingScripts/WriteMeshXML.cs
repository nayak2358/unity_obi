using UnityEngine;
using System.IO;
using System.Xml.Serialization;
using System.Xml;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Obi;
using Valve.VR;
using RosSharp;

public class WriteMeshXML : MonoBehaviour
{
    public SteamVR_Action_Boolean m_StartRecording = null;
    public string name;
    public ObiCloth cloth;
    public List<Transform> TrackedObjects = new List<Transform>();
    public float timesteps = 1;

    private Vector3 position;
    private Quaternion rotation;
    private Mesh UsedMesh;
    private bool record = false;
    private Frame currentFrame = new Frame();

    private Vector3[] nodespositions;

    private Controller ControllerRight = new Controller();
    private Controller ControllerLeft = new Controller();
    private GripPointXML GripPoint1 = new GripPointXML();
    private GripPointXML GripPoint2 = new GripPointXML();
    private GripPointXML GripPoint3 = new GripPointXML();
    private GripPointXML GripPoint4 = new GripPointXML();
    private GripPointXML GripPoint5 = new GripPointXML();
    private GripPointXML GripPoint6 = new GripPointXML();
    private TrackedObject table = new TrackedObject();
    private MeshNodes Nodes = new MeshNodes();

    private Exp Exper = new Exp();
    private List<Frame> framelist = new List<Frame>();

    private Interactable cube;
    private FixedJoint Joint;

    private float currentTime = 0f;
    private XmlSerializer serializer;
    private StreamWriter writer;

    private int itern = 0;

    void Start()
    {
        UsedMesh = cloth.clothBlueprint.inputMesh;

        //MeshTriangles mesh = new MeshTriangles();
        //mesh.triangles = UsedMesh.triangles;
        

        ControllerRight.name = "Right";
        ControllerLeft.name = "Left";
        GripPoint1.name = "BL";
        GripPoint2.name = "TL";
        GripPoint3.name = "ML";
        GripPoint4.name = "TR";
        GripPoint5.name = "BR";
        GripPoint6.name = "MR";
        table.name = "Table";
        Nodes.total = UsedMesh.vertices.Length;

        //Exper.name = "abc";

        nodespositions = new Vector3[Nodes.total];

        
        serializer = new XmlSerializer(typeof(Exp));
        
        writer = new StreamWriter(name + ".xml");
    }
    void Update()
    {
        

        if (m_StartRecording.lastStateDown)
        {
            if (!record)
            {
                record = true;
                Debug.Log("Start");
            }
            else
            {
                record = false;
                Debug.Log("End");
                Exper.Frames = framelist;
                Exper.MeshVertices = UsedMesh.vertices;
                Exper.MeshTriangles = UsedMesh.triangles;
                Exper.Name = "hola";
                XmlSerializerNamespaces ns = new XmlSerializerNamespaces();
                ns.Add("", "");
                var settings = new XmlWriterSettings();
                settings.Indent = true;
                settings.OmitXmlDeclaration = true;
                serializer.Serialize(writer.BaseStream, Exper, ns);
                writer.Close();
            }
        }
        
        if (record && Time.time >= currentTime + timesteps)
        {
            currentTime = Time.time;
            ControllerRight.triggValue = TrackedObjects[8].parent.gameObject.GetComponent<ControllerGrab>().triggerValue;
            ControllerRight.position = TrackedObjects[8].parent.position.Unity2Ros();
            ControllerRight.rotation = TrackedObjects[8].parent.rotation.Unity2Ros().eulerAngles;
            Joint = TrackedObjects[8].parent.gameObject.GetComponent<FixedJoint>();
            if (Joint.connectedBody != null)
            {
                ControllerRight.grabbing = Joint.connectedBody.gameObject.name;
            }
            else
            {
                ControllerRight.grabbing = "None";
            }

            ControllerLeft.triggValue = TrackedObjects[7].parent.gameObject.GetComponent<ControllerGrab>().triggerValue;
            ControllerLeft.position = TrackedObjects[7].parent.position.Unity2Ros();
            ControllerLeft.rotation = TrackedObjects[7].parent.rotation.Unity2Ros().eulerAngles;
            Joint = TrackedObjects[7].parent.gameObject.GetComponent<FixedJoint>();
            if (Joint.connectedBody != null)
            {
                ControllerLeft.grabbing = Joint.connectedBody.gameObject.name;
            }
            else
            {
                ControllerLeft.grabbing = "None";
            }

            table.position = TrackedObjects[0].position.Unity2Ros();
            table.rotation = TrackedObjects[0].rotation.Unity2Ros().eulerAngles;

            for (int i = 0; i < UsedMesh.vertices.Length; i++)
            {
                int solverIndex = cloth.solverIndices[i];
                nodespositions[i] = cloth.GetParticlePosition(solverIndex).Unity2Ros();
            }
            
            Nodes.positions = nodespositions;

            GripPoint1.position = TrackedObjects[1].position.Unity2Ros();
            GripPoint1.rotation = TrackedObjects[1].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[1].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint1.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint1.grabedBy = "None";
            }

            GripPoint2.position = TrackedObjects[2].position.Unity2Ros();
            GripPoint2.rotation = TrackedObjects[2].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[2].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint2.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint2.grabedBy = "None";
            }

            GripPoint3.position = TrackedObjects[3].position.Unity2Ros();
            GripPoint3.rotation = TrackedObjects[3].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[3].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint3.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint3.grabedBy = "None";
            }

            GripPoint4.position = TrackedObjects[4].position.Unity2Ros();
            GripPoint4.rotation = TrackedObjects[4].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[4].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint4.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint4.grabedBy = "None";
            }

            GripPoint5.position = TrackedObjects[5].position.Unity2Ros();
            GripPoint5.rotation = TrackedObjects[5].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[5].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint5.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint5.grabedBy = "None";
            }

            GripPoint6.position = TrackedObjects[6].position.Unity2Ros();
            GripPoint6.rotation = TrackedObjects[6].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[6].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint6.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint6.grabedBy = "None";
            }

            currentFrame = new Frame();

            currentFrame.GarmentName = "SmallTowel";
            currentFrame.TimeStamp = Time.time;
            currentFrame.ControllerLeft = ControllerLeft;
            currentFrame.ControllerRight = ControllerRight;
            currentFrame.GripPoint1 = GripPoint1;
            currentFrame.GripPoint2 = GripPoint2;
            currentFrame.GripPoint3 = GripPoint3;
            currentFrame.GripPoint4 = GripPoint4;
            currentFrame.GripPoint5 = GripPoint5;
            currentFrame.GripPoint6 = GripPoint6;
            currentFrame.Table = table;
            currentFrame.Nodes = Nodes;

            framelist.Add(currentFrame.Clone());

            
            
        }
    }
    void End()
    {
        
    }
}

public class Exp
{
    public string Name;

    public Vector3[] MeshVertices;

    public int[] MeshTriangles;

    public List<Frame> Frames = new List<Frame>();    
}

public class Frame
{
    public string GarmentName;

    public float TimeStamp;

    public Controller ControllerRight;

    public Controller ControllerLeft;

    public GripPointXML GripPoint1;

    public GripPointXML GripPoint2;

    public GripPointXML GripPoint3;

    public GripPointXML GripPoint4;

    public GripPointXML GripPoint5;

    public GripPointXML GripPoint6;

    public TrackedObject Table;

    public MeshNodes Nodes;

    public Frame Clone()
    {
        Frame other = new Frame();
        other.GarmentName = this.GarmentName;
        other.TimeStamp = this.TimeStamp;
        other.ControllerRight = this.ControllerRight.Clone();
        other.ControllerLeft = this.ControllerLeft.Clone();
        other.GripPoint1 = this.GripPoint1.Clone();
        other.GripPoint2 = this.GripPoint2.Clone();
        other.GripPoint3 = this.GripPoint3.Clone();
        other.GripPoint4 = this.GripPoint4.Clone();
        other.GripPoint5 = this.GripPoint5.Clone();
        other.GripPoint6 = this.GripPoint6.Clone();
        other.Table = this.Table.Clone();
        other.Nodes = this.Nodes.Clone();

        return other;
    }
}

public class Controller
{
    public string type = "Controller";
    public string name;
    public Vector3 position;
    public Vector3 rotation;
    public string grabbing;
    public float triggValue;

    public Controller Clone()
    {
        Controller other = new Controller();
        other.type = this.type;
        other.name = this.name;
        other.position = this.position;
        other.rotation = this.rotation;
        other.grabbing = this.grabbing;
        other.triggValue = this.triggValue;

        return other;
    }
}

public class GripPointXML
{
    public string type = "GripPoint";
    public string name;
    public Vector3 position;
    public Vector3 rotation;
    public string grabedBy;

    public GripPointXML Clone()
    {
        GripPointXML other = new GripPointXML();
        other.type = this.type;
        other.name = this.name;
        other.position = this.position;
        other.rotation = this.rotation;
        other.grabedBy = this.grabedBy;

        return other;
    }
}

public class TrackedObject
{
    public string type = "TrackedObject";
    public string name;
    public Vector3 position;
    public Vector3 rotation;

    public TrackedObject Clone()
    {
        TrackedObject other = new TrackedObject();
        other.type = this.type;
        other.name = this.name;
        other.position = this.position;
        other.rotation = this.rotation;

        return other;
    }
}

public class MeshNodes
{
    public int total;
    public Vector3[] positions;

    public MeshNodes Clone()
    {
        MeshNodes other = new MeshNodes();
        other.total = this.total;
        other.positions = this.positions;

        return other;
    }
}

public class MeshTriangles
{
    public int[] triangles;

}



