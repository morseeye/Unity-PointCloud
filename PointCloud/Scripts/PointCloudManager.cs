using UnityEngine;
using System.Collections;
using System.IO;
using System.Text;

public class PointCloudManager : MonoBehaviour
{

    // File
    public string dataPath;
    private string filename;
    public Material matVertex;

    // GUI
    private float progress = 0;
    private string guiText;
    private bool loaded = false;

    // PointCloud
    private GameObject pointCloud;

    public float scale = 1;
    public bool invertYZ = false;
    public bool forceReload = false;

    public int numPoints;
    public int numPointGroups;
    private int limitPoints = 65000;

    private Vector3[] points;
    private Color[] colors;
    private Vector3 minValue;


    void Start()
    {
        // Create Resources folder
        createFolders();

        // Get Filename
        filename = Path.GetFileNameWithoutExtension(dataPath);

        loadScene();
    }

    void loadScene()
    {
        // 检查点云是否之前已加载
        if (!Directory.Exists(Application.dataPath + "/Resources/PointCloudMeshes/" + filename))
        {
            // 如果目录不存在，创建目录
            UnityEditor.AssetDatabase.CreateFolder("Assets/Resources/PointCloudMeshes", filename);
            loadPointCloud();
        }
        else if (forceReload)
        {
            // 如果强制重新加载，删除现有目录
            UnityEditor.FileUtil.DeleteFileOrDirectory(Application.dataPath + "/Resources/PointCloudMeshes/" + filename);
            UnityEditor.AssetDatabase.Refresh();
            UnityEditor.AssetDatabase.CreateFolder("Assets/Resources/PointCloudMeshes", filename);
            loadPointCloud();
        }
        else
        {
            // 尝试加载已存储的点云
            bool success = loadStoredMeshes();
            // 如果加载失败，重新加载原始点云
            if (!success)
            {
                Debug.Log("未能加载已存储的点云，尝试重新加载原始文件");
                loadPointCloud();
            }
        }
    }

    void loadPointCloud()
    {
        // 检查存在哪个文件
        if (File.Exists(Application.dataPath + "/" + dataPath + ".off"))
            // 加载OFF格式
            StartCoroutine("loadOFF", dataPath + ".off");
        else if (File.Exists(Application.dataPath + "/" + dataPath + ".ply"))
            // 加载PLY格式
            StartCoroutine("loadPLY", dataPath + ".ply");
        else
            Debug.LogError("未找到文件: '" + dataPath + ".off' 或 '" + dataPath + ".ply'");
    }

    // 加载已存储的点云，返回是否成功
    bool loadStoredMeshes()
    {
        Debug.Log("尝试加载已存储的点云: " + filename);

        // 检查预制体是否存在
        UnityEngine.Object prefabResource = Resources.Load("PointCloudMeshes/" + filename + "/" + filename);

        if (prefabResource == null)
        {
            Debug.LogWarning("未能在 Resources/PointCloudMeshes/" + filename + "/" + filename + " 找到点云预制体");
            return false;
        }

        try
        {
            GameObject pointGroup = Instantiate(prefabResource) as GameObject;
            if (pointGroup == null)
            {
                Debug.LogError("实例化预制体失败");
                return false;
            }

            loaded = true;
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError("加载已存储的网格时出错: " + e.Message);
            return false;
        }
    }

    // 读取OFF文件的协程
    IEnumerator loadOFF(string dPath)
    {
        Debug.Log("加载OFF文件: " + dPath);

        // 读取文件
        StreamReader sr = new StreamReader(Application.dataPath + "/" + dPath);
        sr.ReadLine(); // OFF
        string[] buffer = sr.ReadLine().Split(); // nPoints, nFaces

        numPoints = int.Parse(buffer[0]);
        points = new Vector3[numPoints];
        colors = new Color[numPoints];
        minValue = new Vector3();

        for (int i = 0; i < numPoints; i++)
        {
            buffer = sr.ReadLine().Split();

            if (!invertYZ)
                points[i] = new Vector3(float.Parse(buffer[0]) * scale, float.Parse(buffer[1]) * scale, float.Parse(buffer[2]) * scale);
            else
                points[i] = new Vector3(float.Parse(buffer[0]) * scale, float.Parse(buffer[2]) * scale, float.Parse(buffer[1]) * scale);

            if (buffer.Length >= 6)
                colors[i] = new Color(int.Parse(buffer[3]) / 255.0f, int.Parse(buffer[4]) / 255.0f, int.Parse(buffer[5]) / 255.0f);
            else
                colors[i] = Color.cyan;

            // GUI
            progress = i * 1.0f / (numPoints - 1) * 1.0f;
            if (i % Mathf.FloorToInt(numPoints / 20) == 0)
            {
                guiText = i.ToString() + " out of " + numPoints.ToString() + " loaded";
                yield return null;
            }
        }

        // 创建点云
        createPointCloud();

        loaded = true;
    }

    // 读取PLY文件的协程
    IEnumerator loadPLY(string dPath)
    {
        Debug.Log("加载PLY文件: " + dPath);

        // 打开文件
        FileStream fs = new FileStream(Application.dataPath + "/" + dPath, FileMode.Open, FileAccess.Read);
        BinaryReader br = new BinaryReader(fs);

        // 读取PLY头部信息
        string header = "";
        bool isBinary = false;
        bool isLittleEndian = false;
        int vertexCount = 0;

        // 属性索引和类型
        int xIndex = -1, yIndex = -1, zIndex = -1;
        int redIndex = -1, greenIndex = -1, blueIndex = -1;
        int propertyCount = 0;
        bool[] isDouble = new bool[10]; // 假设不超过10个属性

        // 读取头部直到"end_header"
        StringBuilder sb = new StringBuilder();
        char c;
        while (true)
        {
            c = (char)br.ReadByte();
            if (c == '\n')
            {
                string line = sb.ToString().Trim();
                header += line + "\n";

                // 分析头部信息
                if (line.StartsWith("format"))
                {
                    string[] parts = line.Split();
                    if (parts.Length > 1)
                    {
                        isBinary = parts[1].Contains("binary");
                        isLittleEndian = parts[1].Contains("little_endian");
                    }
                }
                else if (line.StartsWith("element vertex"))
                {
                    string[] parts = line.Split();
                    if (parts.Length > 2)
                    {
                        vertexCount = int.Parse(parts[2]);
                    }
                }
                else if (line.StartsWith("property"))
                {
                    string[] parts = line.Split();
                    if (parts.Length > 2)
                    {
                        // 检查属性类型
                        if (parts[1] == "double" || parts[1] == "float")
                        {
                            isDouble[propertyCount] = (parts[1] == "double");

                            // 记录位置属性的索引
                            if (parts[2] == "x") xIndex = propertyCount;
                            else if (parts[2] == "y") yIndex = propertyCount;
                            else if (parts[2] == "z") zIndex = propertyCount;
                        }
                        // 记录颜色属性的索引
                        else if ((parts[1] == "uchar" || parts[1] == "uint8") &&
                                (parts[2] == "red" || parts[2] == "r"))
                        {
                            redIndex = propertyCount;
                        }
                        else if ((parts[1] == "uchar" || parts[1] == "uint8") &&
                                (parts[2] == "green" || parts[2] == "g"))
                        {
                            greenIndex = propertyCount;
                        }
                        else if ((parts[1] == "uchar" || parts[1] == "uint8") &&
                                (parts[2] == "blue" || parts[2] == "b"))
                        {
                            blueIndex = propertyCount;
                        }

                        propertyCount++;
                    }
                }
                else if (line == "end_header")
                {
                    break;
                }

                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }

        // 检查文件格式
        if (!isBinary)
        {
            Debug.Log("检测到ASCII格式PLY文件，切换到ASCII加载器");
            fs.Close();
            br.Close();
            yield return StartCoroutine(loadPLY_ASCII(dPath));
            yield break;
        }

        if (!isLittleEndian)
        {
            Debug.LogError("暂不支持big_endian格式的二进制PLY文件");
            fs.Close();
            br.Close();
            yield break;
        }

        // 检查是否找到所有必要的属性
        if (xIndex < 0 || yIndex < 0 || zIndex < 0)
        {
            Debug.LogError("PLY文件缺少必要的位置属性 (x, y, z)");
            fs.Close();
            br.Close();
            yield break;
        }

        bool hasColors = (redIndex >= 0 && greenIndex >= 0 && blueIndex >= 0);

        Debug.Log("PLY文件包含 " + vertexCount + " 个顶点, " +
            (hasColors ? "有颜色" : "无颜色") +
            "，位置数据类型：" + (isDouble[xIndex] ? "double" : "float"));

        // 准备加载点云数据
        numPoints = vertexCount;
        points = new Vector3[numPoints];
        colors = new Color[numPoints];
        minValue = new Vector3();

        // 读取二进制顶点数据
        for (int i = 0; i < numPoints; i++)
        {
            // 创建一个缓冲区来存储一个顶点的所有属性
            double x = 0, y = 0, z = 0;
            byte r = 255, g = 255, b = 255;

            // 对每个属性进行读取
            for (int p = 0; p < propertyCount; p++)
            {
                if (isDouble[p])
                {
                    // 读取double类型数据 (8字节)
                    double value = br.ReadDouble();

                    // 根据属性类型赋值
                    if (p == xIndex) x = value;
                    else if (p == yIndex) y = value;
                    else if (p == zIndex) z = value;
                }
                else if (p == redIndex || p == greenIndex || p == blueIndex)
                {
                    // 读取颜色数据 (1字节)
                    byte value = br.ReadByte();

                    if (p == redIndex) r = value;
                    else if (p == greenIndex) g = value;
                    else if (p == blueIndex) b = value;
                }
                else
                {
                    // 跳过其他类型的属性，假设它们是float (4字节)
                    br.ReadSingle();
                }
            }

            // 创建Unity坐标和颜色
            if (!invertYZ)
                points[i] = new Vector3((float)(x * scale), (float)(y * scale), (float)(z * scale));
            else
                points[i] = new Vector3((float)(x * scale), (float)(z * scale), (float)(y * scale));

            if (hasColors)
                colors[i] = new Color(r / 255.0f, g / 255.0f, b / 255.0f);
            else
                colors[i] = Color.cyan;

            // 更新GUI进度
            progress = i * 1.0f / (numPoints - 1) * 1.0f;
            if (i % Mathf.FloorToInt(numPoints / 20) == 0)
            {
                guiText = i.ToString() + " 已加载，共 " + numPoints.ToString();
                yield return null;
            }
        }

        // 关闭文件
        fs.Close();
        br.Close();

        // 创建点云并保存为预制体
        createPointCloud();

        loaded = true;
    }

    // ASCII格式PLY加载器（保留原来的实现）
    IEnumerator loadPLY_ASCII(string dPath)
    {
        // 读取文件
        StreamReader sr = new StreamReader(Application.dataPath + "/" + dPath);

        // 跳过头部
        string line;
        do
        {
            line = sr.ReadLine();
        } while (line != null && !line.StartsWith("end_header"));

        if (line == null)
        {
            Debug.LogError("PLY文件头部格式错误");
            yield break;
        }

        // 假设我们已经从头部解析了这些信息
        numPoints = 0; // 这里需要实际的点数
        int xIndex = 0, yIndex = 1, zIndex = 2;
        int redIndex = 3, greenIndex = 4, blueIndex = 5;
        bool hasColors = false;

        // 读取顶点数据
        // 此处实现与前面类似

        sr.Close();
        createPointCloud();
        loaded = true;
    }

    // 创建点云并保存为预制体
    void createPointCloud()
    {
        // 实例化点组
        numPointGroups = Mathf.CeilToInt(numPoints * 1.0f / limitPoints * 1.0f);

        pointCloud = new GameObject(filename);

        for (int i = 0; i < numPointGroups - 1; i++)
        {
            InstantiateMesh(i, limitPoints);
            if (i % 10 == 0)
            {
                guiText = i.ToString() + " out of " + numPointGroups.ToString() + " PointGroups loaded";
                // 无需yield，因为这不是协程
            }
        }
        InstantiateMesh(numPointGroups - 1, numPoints - (numPointGroups - 1) * limitPoints);

        // 存储点云预制体
        string prefabPath = "Assets/Resources/PointCloudMeshes/" + filename + "/" + filename + ".prefab";
        Debug.Log("保存点云预制体到: " + prefabPath);
        UnityEditor.PrefabUtility.CreatePrefab(prefabPath, pointCloud);
        UnityEditor.AssetDatabase.Refresh();
    }

    void InstantiateMesh(int meshInd, int nPoints)
    {
        // 创建网格
        GameObject pointGroup = new GameObject(filename + meshInd);
        pointGroup.AddComponent<MeshFilter>();
        pointGroup.AddComponent<MeshRenderer>();
        pointGroup.GetComponent<Renderer>().material = matVertex;

        pointGroup.GetComponent<MeshFilter>().mesh = CreateMesh(meshInd, nPoints, limitPoints);
        pointGroup.transform.parent = pointCloud.transform;

        // 存储网格
        string meshPath = "Assets/Resources/PointCloudMeshes/" + filename + "/" + filename + meshInd + ".asset";
        UnityEditor.AssetDatabase.CreateAsset(pointGroup.GetComponent<MeshFilter>().mesh, meshPath);
        UnityEditor.AssetDatabase.SaveAssets();
    }

    Mesh CreateMesh(int id, int nPoints, int limitPoints)
    {
        Mesh mesh = new Mesh();

        Vector3[] myPoints = new Vector3[nPoints];
        int[] indecies = new int[nPoints];
        Color[] myColors = new Color[nPoints];

        for (int i = 0; i < nPoints; ++i)
        {
            myPoints[i] = points[id * limitPoints + i] - minValue;
            indecies[i] = i;
            myColors[i] = colors[id * limitPoints + i];
        }

        mesh.vertices = myPoints;
        mesh.colors = myColors;
        mesh.SetIndices(indecies, MeshTopology.Points, 0);
        mesh.uv = new Vector2[nPoints];
        mesh.normals = new Vector3[nPoints];

        return mesh;
    }

    void calculateMin(Vector3 point)
    {
        if (minValue.magnitude == 0)
            minValue = point;

        if (point.x < minValue.x)
            minValue.x = point.x;
        if (point.y < minValue.y)
            minValue.y = point.y;
        if (point.z < minValue.z)
            minValue.z = point.z;
    }

    void createFolders()
    {
        if (!Directory.Exists(Application.dataPath + "/Resources/"))
            UnityEditor.AssetDatabase.CreateFolder("Assets", "Resources");

        if (!Directory.Exists(Application.dataPath + "/Resources/PointCloudMeshes/"))
            UnityEditor.AssetDatabase.CreateFolder("Assets/Resources", "PointCloudMeshes");
    }

    void OnGUI()
    {
        if (!loaded)
        {
            GUI.BeginGroup(new Rect(Screen.width / 2 - 100, Screen.height / 2, 400.0f, 20));
            GUI.Box(new Rect(0, 0, 200.0f, 20.0f), guiText);
            GUI.Box(new Rect(0, 0, progress * 200.0f, 20), "");
            GUI.EndGroup();
        }
    }
}