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

public class WriteMeshXMLOgreL : MonoBehaviour
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
    private FrameOgreL currentFrame = new FrameOgreL();
    private geometry currentgeometry = new geometry();
    private vertexbuffer currentvertexbuffer = new vertexbuffer();
    private vertex[] currentvertex;
    private vertex currentvertexi = new vertex();
    private position currentpositions = new position();

    private Vector3[] nodespositions;

    private Vector3 UnitytoRos = new Vector3();

    private Controller ControllerRight = new Controller();
    private Controller ControllerLeft = new Controller();
    private GripPointXML GripPoint1 = new GripPointXML();
    private GripPointXML GripPoint2 = new GripPointXML();
    private GripPointXML GripPoint3 = new GripPointXML();
    private GripPointXML GripPoint4 = new GripPointXML();
    private GripPointXML GripPoint5 = new GripPointXML();
    private GripPointXML GripPoint6 = new GripPointXML();
    private GripPointXML GripPoint7 = new GripPointXML();
    private GripPointXML GripPoint8 = new GripPointXML();
    private TrackedObject table = new TrackedObject();
    private MeshNodes Nodes = new MeshNodes();

    private meshes OgreMesh = new meshes();
    private faces faces = new faces();
    private geometry geometry = new geometry();
    private face[] face;
    private face facei = new face();
    private vertexbuffer vertexbuffer = new vertexbuffer();
    private vertex[] vertex;
    private vertex vertexi = new vertex();
    private position positions = new position();

    private ManipulationL Exper = new ManipulationL();
    private List<FrameOgreL> framelist = new List<FrameOgreL>();

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
        GripPoint7.name = "LR";
        GripPoint8.name = "LL";
        table.name = "Table";
        Nodes.total = UsedMesh.vertices.Length;

        nodespositions = new Vector3[Nodes.total];


        serializer = new XmlSerializer(typeof(ManipulationL));

        writer = new StreamWriter(name + ".xml");
    }
    void Update()
    {


        if (m_StartRecording.lastStateDown || Input.GetKeyDown("space"))
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

                vertex = new vertex[UsedMesh.vertices.Length];
                for (int i = 0; i < UsedMesh.vertices.Length; i++)
                {
                    positions = new position();
                    vertexi = new vertex();
                    UnitytoRos = UsedMesh.vertices[i].Unity2Ros();
                    positions.x = UnitytoRos.x;
                    positions.y = UnitytoRos.y;
                    positions.z = UnitytoRos.z;
                    vertexi.position = positions;
                    vertex[i] = vertexi;
                }

                vertexbuffer.positions = "true";
                vertexbuffer.normals = "false";
                vertexbuffer.vertices = vertex;

                geometry.vertexcount = UsedMesh.vertices.Length;
                geometry.vertexbuffer = vertexbuffer;

                face = new face[UsedMesh.triangles.Length / 3];
                for (int i = 0; i < UsedMesh.triangles.Length / 3; i++)
                {
                    facei = new face();
                    facei.v1 = UsedMesh.triangles[i * 3];
                    facei.v2 = UsedMesh.triangles[i * 3 + 1];
                    facei.v3 = UsedMesh.triangles[i * 3 + 2];
                    face[i] = facei;
                }

                faces.count = UsedMesh.triangles.Length / 3;
                faces.faces_list = face;

                OgreMesh.material = "Null";
                OgreMesh.useshardvertices = "false";
                OgreMesh.faces = faces;
                OgreMesh.geometry = geometry;

                Exper.mesh = OgreMesh;
                Exper.Frames = framelist;
                Exper.Name = name;
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
            ControllerRight.triggValue = TrackedObjects[9].parent.gameObject.GetComponent<ControllerGrab>().triggerValue;
            ControllerRight.position = TrackedObjects[9].parent.position.Unity2Ros();
            ControllerRight.rotation = TrackedObjects[9].parent.rotation.Unity2Ros().eulerAngles;
            Joint = TrackedObjects[9].parent.gameObject.GetComponent<FixedJoint>();
            if (Joint.connectedBody != null)
            {
                ControllerRight.grabbing = Joint.connectedBody.gameObject.name;
            }
            else
            {
                ControllerRight.grabbing = "None";
            }

            ControllerLeft.triggValue = TrackedObjects[10].parent.gameObject.GetComponent<ControllerGrab>().triggerValue;
            ControllerLeft.position = TrackedObjects[10].parent.position.Unity2Ros();
            ControllerLeft.rotation = TrackedObjects[10].parent.rotation.Unity2Ros().eulerAngles;
            Joint = TrackedObjects[10].parent.gameObject.GetComponent<FixedJoint>();
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

            currentvertex = new vertex[UsedMesh.vertices.Length];
            for (int i = 0; i < UsedMesh.vertices.Length; i++)
            {
                currentpositions = new position();
                currentvertexi = new vertex();
                currentpositions.x = nodespositions[i].x;
                currentpositions.y = nodespositions[i].y;
                currentpositions.z = nodespositions[i].z;
                currentvertexi.position = currentpositions;
                currentvertex[i] = currentvertexi;
            }

            currentvertexbuffer.positions = "true";
            currentvertexbuffer.normals = "false";
            currentvertexbuffer.vertices = currentvertex;

            currentgeometry.vertexcount = UsedMesh.vertices.Length;
            currentgeometry.vertexbuffer = currentvertexbuffer;

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

            GripPoint7.position = TrackedObjects[7].position.Unity2Ros();
            GripPoint7.rotation = TrackedObjects[7].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[7].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint7.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint7.grabedBy = "None";
            }

            GripPoint8.position = TrackedObjects[8].position.Unity2Ros();
            GripPoint8.rotation = TrackedObjects[8].rotation.Unity2Ros().eulerAngles;
            cube = TrackedObjects[8].gameObject.GetComponent<Interactable>();
            if (cube.m_ActiveController != null)
            {
                GripPoint8.grabedBy = cube.m_ActiveController.gameObject.name;
            }
            else
            {
                GripPoint8.grabedBy = "None";
            }

            currentFrame = new FrameOgreL();

            currentFrame.GarmentName = "Garment";
            currentFrame.TimeStamp = Time.time;
            currentFrame.ControllerLeft = ControllerLeft;
            currentFrame.ControllerRight = ControllerRight;
            currentFrame.GripPoint1 = GripPoint1;
            currentFrame.GripPoint2 = GripPoint2;
            currentFrame.GripPoint3 = GripPoint3;
            currentFrame.GripPoint4 = GripPoint4;
            currentFrame.GripPoint5 = GripPoint5;
            currentFrame.GripPoint6 = GripPoint6;
            currentFrame.GripPoint7 = GripPoint7;
            currentFrame.GripPoint8 = GripPoint8;
            currentFrame.Table = table;
            currentFrame.geometry = currentgeometry;

            framelist.Add(currentFrame.Clone());

        }
    }
    void End()
    {

    }
}

public class ManipulationL
{
    public string Name;

    public meshes mesh;

    public List<FrameOgreL> Frames = new List<FrameOgreL>();
}

public class FrameOgreL
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

    public GripPointXML GripPoint7;

    public GripPointXML GripPoint8;

    public TrackedObject Table;

    public geometry geometry;

    public FrameOgreL Clone()
    {
        FrameOgreL other = new FrameOgreL();
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
        other.GripPoint7 = this.GripPoint7.Clone();
        other.GripPoint8 = this.GripPoint8.Clone();
        other.Table = this.Table.Clone();
        other.geometry = this.geometry.Clone();

        return other;
    }
}
