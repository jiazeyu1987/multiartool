// Enhanced server with heartbeats and dual control/data sockets
using System;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections;
using System.Linq;
using Newtonsoft.Json;
using TMPro;
using UnityEngine.SceneManagement;
public class HololensCommand
{
    public string type { get; set; }
    public string model_name { get; set; }
    public string child_name { get; set; }
    public float opacity { get; set; }
    public float colorR { get; set; }
    public float colorG { get; set; }
    public float colorB { get; set; }
}
public class ModelReceiverServer : MonoBehaviour
{
    public int port = 9000;
    public int controlPort = 9100;
    public int heartbeatPort = 9200;

    private TcpListener listener;
    private TcpListener controlListener;
    private TcpListener heartbeatListener;

    private Thread listenerThread;
    private Thread controlThread;
    private Thread heartbeatThread;
    private List<string> skinChildNames = new();  // 存储 name.txt 中的名字
    private string persistentRootPath;
    private readonly object connectionLock = new();
    private readonly object modelInfoLock = new();
    int addedPointCount = 0;
    private Stack<GameObject> pointStack = new(); // 添加栈结构记录小球

    private Dictionary<string, GameObject> loadedModels = new();
    private Dictionary<string, List<Dictionary<string, object>>> modelInfoCache = new();
    private List<TcpClient> heartbeatClients = new();

    void Start()
    {
        persistentRootPath = Application.persistentDataPath;
        UnityMainThreadDispatcher.Instance();

        listenerThread = new Thread(ListenForConnection) { IsBackground = true };
        listenerThread.Start();

        controlThread = new Thread(ListenForControl) { IsBackground = true };
        controlThread.Start();

        heartbeatThread = new Thread(ListenForHeartbeat) { IsBackground = true };
        heartbeatThread.Start();
    }

    void ListenForConnection()
    {
        listener = new TcpListener(IPAddress.Any, port);
        listener.Start();
        while (true)
        {
            TcpClient client = listener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(HandleFileClient, client);
        }
    }

    void HandleFileClient(object obj)
    {
        TcpClient client = obj as TcpClient;
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            byte[] sizeBuffer = new byte[8];
            while (stream.CanRead && ReadExact(stream, sizeBuffer, 8))
            {
                if (BitConverter.IsLittleEndian) Array.Reverse(sizeBuffer);
                long fileSize = BitConverter.ToInt64(sizeBuffer, 0);

                byte[] nameLenBuffer = new byte[2];
                if (!ReadExact(stream, nameLenBuffer, 2)) break;
                ushort nameLength = (ushort)((nameLenBuffer[0] << 8) | nameLenBuffer[1]);

                byte[] nameBuffer = new byte[nameLength];
                if (!ReadExact(stream, nameBuffer, nameLength)) break;
                string fileName = SanitizeFileName(Encoding.UTF8.GetString(nameBuffer));
                string savePath = Path.Combine(persistentRootPath, fileName);

                using (FileStream fs = new(savePath, FileMode.Create))
                {
                    byte[] buffer = new byte[4096];
                    long total = 0;
                    while (total < fileSize)
                    {
                        int read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, fileSize - total));
                        if (read <= 0) break;
                        fs.Write(buffer, 0, read);
                        total += read;
                    }
                }

                if (fileName.EndsWith(".obj"))
                {
                    UnityMainThreadDispatcher.Instance().Enqueue(() => LoadAndShowModel(savePath, fileName));
                }
                else if (fileName.Equals("name.txt", StringComparison.OrdinalIgnoreCase))
                {
                    skinChildNames.Clear();
                    skinChildNames.AddRange(File.ReadAllLines(savePath).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim()));
                    Debug.Log("📃 name.txt loaded with " + skinChildNames.Count + " names.");
                }
            }
        }
    }

    void ListenForControl()
    {
        controlListener = new TcpListener(IPAddress.Any, controlPort);
        controlListener.Start();

        while (true)
        {
            TcpClient client = controlListener.AcceptTcpClient();
            ThreadPool.QueueUserWorkItem(HandleControlClient, client);
        }
    }

    void HandleControlClient(object obj)
    {
        TcpClient client = obj as TcpClient;
        try
        {
            using StreamReader reader = new(client.GetStream(), Encoding.UTF8);
            while (true)
            {
                string line = reader.ReadLine();
                if (string.IsNullOrEmpty(line)) break;

                Debug.Log("📝 Received from client: " + line);

                
                {
                    var command = JsonConvert.DeserializeObject<HololensCommand>(line);

                    if (command.type == "update_opacity")
                    {
                        string modelName = command.model_name.ToLower();
                        string childName = command.child_name;
                        float opacity = command.opacity;

                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            if (loadedModels.TryGetValue(modelName, out GameObject model))
                            {
                                Transform childTransform = model.transform.Find(childName);
                                if (childTransform != null)
                                {
                                    var renderer = childTransform.GetComponent<MeshRenderer>();
                                    if (renderer != null && renderer.material != null)
                                    {
                                        if (opacity < 1.0f)
                                        {
                                            SetMaterialTransparent(renderer.material, opacity);
                                        }
                                        else
                                        {
                                            SetMaterialOpaque(renderer.material); // 🔁 恢复为完全不透明
                                            // 只设置为不透明颜色，但不设置透明渲染模式
                                            var color = renderer.material.color;
                                            color.a = 1.0f;
                                            renderer.material.color = color;
                                        }

                                        Debug.Log($"✅ Updated opacity of {modelName}/{childName} to {opacity}");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"⚠️ Renderer or material missing on {modelName}/{childName}");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"⚠️ Child '{childName}' not found in model '{modelName}'");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"⚠️ Model '{modelName}' not found.");
                            }
                        });
                    }
                    else if (command.type == "update_color")
                    {
                        string modelName = command.model_name.ToLower();
                        string childName = command.child_name;
                        float r = command.colorR;
                        float g = command.colorG;
                        float b = command.colorB;

                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            if (loadedModels.TryGetValue(modelName, out GameObject model))
                            {
                                Transform childTransform = model.transform.Find(childName);
                                if (childTransform != null)
                                {
                                    var renderer = childTransform.GetComponent<MeshRenderer>();
                                    if (renderer != null && renderer.material != null)
                                    {
                                        var color = renderer.material.color;
                                        color.r = r;
                                        color.g = g;
                                        color.b = b;
                                        renderer.material.color = color;

                                        Debug.Log($"🎨 Updated color of {modelName}/{childName} to RGB({r},{g},{b})");
                                    }
                                    else
                                    {
                                        Debug.LogWarning($"⚠️ Renderer or material missing on {modelName}/{childName}");
                                    }
                                }
                                else
                                {
                                    Debug.LogWarning($"⚠️ Child '{childName}' not found in model '{modelName}'");
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"⚠️ Model '{modelName}' not found.");
                            }
                        });
                    }
                    else if (command.type == "add_point")
                    {
                        Debug.LogWarning("add point");
                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            SpawnGreenSphereAtCube();
                        });
                    }
                    else if (command.type == "remove_point")
                    {
                        Debug.LogWarning("remove point");
                        UnityMainThreadDispatcher.Instance().Enqueue(() =>
                        {
                            RemoveLastGreenSphere();
                        });
                    }
                    else
                    {
                        Debug.LogWarning($"⚠️ Unknown command type: {command.type}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("❌ Error in control listener: " + ex.Message);
        }
        finally
        {
            client.Close();
        }
    }

    void UpdateInfoMessage()
    {
        GameObject infoMessage = GameObject.Find("InfoMessage");
        var infoText = infoMessage?.GetComponent<TMP_Text>();
        if (infoText != null)
        {
            infoText.text = $"Point Number :{addedPointCount}";
        }
    }

    void RemoveLastGreenSphere()
    {
        if (pointStack.Count > 0)
        {
            GameObject lastSphere = pointStack.Pop();
            Destroy(lastSphere);
            addedPointCount--;
            Debug.Log($"❎ 删除了第 {addedPointCount + 1} 个绿色小球");
        }
        else
        {
            Debug.Log("⚠️ 没有可删除的小球");
        }
        UpdateInfoMessage();
    }


    void SpawnGreenSphereAtCube()
    {
        GameObject cube = GameObject.Find("V2/Cube");
        if (cube == null)
        {
            Debug.LogWarning("❌ 未找到 V2/Cube");
            return;
        }

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.transform.position = cube.transform.position;
        sphere.transform.localScale = Vector3.one * 0.05f;

        Renderer renderer = sphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = Color.green;
        }
        sphere.name = "GreenSphere_" + (++addedPointCount);

        pointStack.Push(sphere);

        Debug.Log($"✅ 添加了第 {addedPointCount} 个绿色小球");
        UpdateInfoMessage();
    }

    void SetMaterialOpaque(Material mat)
    {
        mat.SetFloat("_Mode", 0);  // Opaque
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        mat.SetInt("_ZWrite", 1);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = -1;
    }


    void SetMaterialTransparent(Material mat, float alpha)
    {
        Color color = mat.color;
        color.a = alpha;
        mat.color = color;

        mat.SetFloat("_Mode", 3);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
    }


    void ListenForHeartbeat()
    {
        heartbeatListener = new TcpListener(IPAddress.Any, heartbeatPort);
        heartbeatListener.Start();
        while (true)
        {
            TcpClient client = heartbeatListener.AcceptTcpClient();
            lock (connectionLock) heartbeatClients.Add(client);
            ThreadPool.QueueUserWorkItem(SendHeartbeat, client);
        }
    }

    void SendHeartbeat(object obj)
    {
        TcpClient client = obj as TcpClient;
        using NetworkStream stream = client.GetStream();
        using StreamWriter writer = new(stream, Encoding.UTF8) { AutoFlush = true };

        try
        {
            while (true)
            {
                string json;
                lock (modelInfoLock)
                {
                    // 🔧 只发送 skin 模型的数据
                    if (modelInfoCache.TryGetValue("skin", out var skinInfo))
                    {
                        var filtered = new Dictionary<string, List<Dictionary<string, object>>>
                    {
                        { "skin", skinInfo }
                    };
                        json = JsonConvert.SerializeObject(filtered);
                    }
                    else
                    {
                        json = "{}";
                    }
                }

                writer.WriteLine(json);
                Thread.Sleep(1000);
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Heartbeat lost: " + ex.Message);
        }
        finally
        {
            client.Close();
            lock (connectionLock) heartbeatClients.Remove(client);
        }
    }
    void LoadAndShowModel(string path, string fileName)
    {
        string modelName = Path.GetFileNameWithoutExtension(fileName).ToLower();
        if (loadedModels.TryGetValue(modelName, out GameObject oldObj) && oldObj != null)
        {
            Destroy(oldObj);
            loadedModels.Remove(modelName);
        }
        StartCoroutine(DelayedLoadModel(path, modelName));
    }

    IEnumerator DelayedLoadModel(string path, string modelName)
    {
        yield return new WaitForEndOfFrame();
        GameObject model = new Dummiesman.OBJLoader().Load(path);
        model.name = modelName;
        model.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 1.5f;
        model.transform.rotation = Quaternion.Euler(-90, 180, 0);
        model.transform.localScale = Vector3.one * 0.001f;

        loadedModels[modelName] = model;

        // 如果是 skin 模型，按 name.txt 重命名其子对象
        if (modelName == "skin" && skinChildNames.Count > 0)
        {
            int count = 0;
            foreach (Transform child in model.transform)
            {
                if (count < skinChildNames.Count)
                {
                    string newName = skinChildNames[count];
                    Debug.Log($"🔤 Renaming child {child.name} -> {newName}");
                    child.name = newName;

                    // 设置颜色
                    var renderer = child.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.material != null)
                    {
                        Color colorToSet = Color.gray; // 默认色

                        switch (newName)
                        {
                            case "皮肤":
                                colorToSet = new Color(1.0f, 0.8f, 0.6f); // 浅肤色
                                break;
                            case "血肿":
                                colorToSet = new Color(0.6f, 0.0f, 0.0f); // 暗红
                                break;
                            case "通道":
                                colorToSet = new Color(0.0f, 0.6f, 1.0f); // 蓝青色
                                break;
                        }

                        renderer.material.color = colorToSet;
                        Debug.Log($"🎨 Set color of {newName} to {colorToSet}");
                    }

                    count++;
                }
            }
        }
        if (modelName == "mark")
        {
            int total = model.transform.childCount;
            int index = 0;

            foreach (Transform child in model.transform)
            {
                var renderer = child.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    float t = total <= 1 ? 1.0f : (float)index / (total - 1);  // 范围 [0, 1]

                    // 黄色 (1.0, 0.92, 0.016) -> 白色 (1.0, 1.0, 1.0)
                    float r = 1.0f;
                    float g = Mathf.Lerp(0.92f, 1.0f, t);
                    float b = Mathf.Lerp(0.016f, 1.0f, t);

                    Color gradientColor = new Color(r, g, b);
                    renderer.material.color = gradientColor;

                    Debug.Log($"🌈 Set mark child '{child.name}' to color RGB({r:F2}, {g:F2}, {b:F2})");

                    index++;
                }
            }
        }
        // 构建心跳信息缓存
        var childrenInfo = new List<Dictionary<string, object>>();
        foreach (Transform child in model.transform)
        {
            var entry = new Dictionary<string, object> { ["name"] = child.name };
            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                var c = renderer.material.color;
                entry["color"] = new float[] { c.r, c.g, c.b };
                entry["alpha"] = c.a;
            }
            childrenInfo.Add(entry);
        }
        lock (modelInfoLock)
        {
            modelInfoCache[modelName] = childrenInfo;
        }
    }
    string SanitizeFileName(string fileName)
    {
        return string.Concat(fileName.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
    }

    bool ReadExact(NetworkStream stream, byte[] buffer, int count)
    {
        int offset = 0;
        while (offset < count)
        {
            int read = stream.Read(buffer, offset, count - offset);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    void OnApplicationQuit()
    {
        listener?.Stop();
        controlListener?.Stop();
        heartbeatListener?.Stop();
        listenerThread?.Abort();
        controlThread?.Abort();
        heartbeatThread?.Abort();
    }
}
