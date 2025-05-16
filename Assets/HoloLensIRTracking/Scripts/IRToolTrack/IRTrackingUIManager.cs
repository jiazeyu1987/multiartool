using UnityEngine;
using TMPro;
using System.Net;
using System.Net.Sockets;
using System.Text;
using IRToolTrack;

public class IRTrackingUIManager : MonoBehaviour
{
    public IRToolTracking trackingManager; // 可选绑定
    private TMP_Text infoText;
    private IRToolController[] tools;
    private string localIPAddress;

    void Start()
    {
        Debug.Log("IRTrackingUIManager.Start() called");

        // 获取文本组件
        GameObject infoMessage = GameObject.Find("InfoMessage");
        if (infoMessage != null)
        {
            infoText = infoMessage.GetComponent<TMP_Text>();
            if (infoText == null)
                Debug.LogWarning("TMP_Text not found on InfoMessage.");
        }
        else
        {
            Debug.LogWarning("InfoMessage GameObject not found.");
        }

        // 获取本机IP
        localIPAddress = GetLocalIPAddress();

        // 获取所有工具
        tools = FindObjectsOfType<IRToolController>();

        // 获取 tracking manager（可选）
        if (trackingManager == null)
            trackingManager = FindObjectOfType<IRToolTracking>();
    }

    void Update()
    {
        return;
        if (infoText == null) return;

        StringBuilder sb = new StringBuilder();

        sb.AppendLine("IP: " + localIPAddress);
        sb.AppendLine("----------------");

        foreach (var tool in tools)
        {
            sb.AppendLine("Tool: " + tool.identifier);
            sb.AppendLine("Tracking: " + (tool.StableTracking ? "Yes" : "No"));

            if (tool.isTracking)
            {
                Matrix4x4 matrix = Matrix4x4.TRS(tool.transform.position, tool.transform.rotation, Vector3.one);

                for (int i = 0; i < 4; i++)
                {
                    sb.Append("[ ");
                    for (int j = 0; j < 4; j++)
                    {
                        sb.Append($"{matrix[i, j]:0.000} ");
                    }
                    sb.AppendLine("]");
                }
            }

            sb.AppendLine("----------------");
        }

        infoText.text = sb.ToString();
    }


    string GetLocalIPAddress()
    {
        string localIP = "Unavailable";
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork)
                {
                    return ip.ToString();
                }
            }
        }
        catch { }
        return localIP;
    }
}
