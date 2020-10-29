using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using BestHTTP;
using BestHTTP.SocketIO;
using System;
using UnityEngine.UI;

/**
 * socket使用类
 */
public class NetWork
{
    private static Socket socket = null;                                       //socket对象
    private static NetWork _inter = null;

    

    public static void init()
    {
        var manager = new SocketManager(new System.Uri("http://127.0.0.1:8889/socket.io/"));
        socket = manager.Socket;
        OnAddEvent();
        // 向服务器发送带有两个参数的自定义事件
        socket.Emit("message", "userName", "message");
        //发送一个事件并定义一个回调函数，该函数将被调用作为对该事件的确认
        socket.Emit("custom event", OnAckCallback, "参数1", "参数2");
    }

    public void OnLogin()
    {
       
    }

    public void OnChat()
    {
        
    }

    private static void OnAddEvent()
    {
        socket.Once("connect", OnConnected);//连接 
        socket.On("Info", OnInfo);
        socket.On("message", OnMessage);//获取消息
        socket.On("Login", OnLogin);//获取登陆消息
        socket.On("Chat", OnChat);//获取聊天消息
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="socket">套接字</param>
    /// <param name="packet">服务器参数</param>
    /// <param name="args"></param>
    private static void OnLogin(Socket socket, Packet packet, params object[] args)
    {
        //socket.EmitAck(packet, "Event", "Received", "Successfully");
        Debug.Log(packet.Payload); //["Login",{"nickName":"123","chatMessage":"Login Scuess"}]
        int index = packet.Payload.IndexOf(',');
        string ss = packet.Payload.Substring(index + 1).Replace(']', ' ');
        JsonData jsonData = JsonUtility.FromJson<JsonData>(ss);
        string message = string.Format("{0} : {1}", jsonData.nickName, jsonData.chatMessage);
        Debug.Log(message);
    }

    private static void OnChat(Socket socket, Packet packet, params object[] args)
    {
        Debug.Log(packet.ToString());
        int index = packet.Payload.IndexOf(',');
        string ss = packet.Payload.Substring(index + 1).Replace(']', ' ');
        JsonData jsonData = JsonUtility.FromJson<JsonData>(ss);
        Debug.Log(jsonData.nickName);
        string message = string.Format("{0} : {1}", jsonData.nickName, jsonData.chatMessage);
    }
    private static void OnConnected(Socket socket, Packet packet, params object[] args)
    {
        Debug.Log("连接上");
    }

    private static void OnInfo(Socket socket, Packet packet, params object[] args)
    {
        Debug.Log(packet.ToString());
        Debug.Log(args.ToString());

    }

    private static void  OnMessage(Socket socket, Packet packet, params object[] args)
    {
        // args[0] is the nick of the sender 
        // args[1] is the message 
        Debug.Log(string.Format("Message from {0}: {1}", args[0], args[1]));
    }

    private static void OnAckCallback(Socket socket, Packet originalPacket, params object[] args)
    {
        Debug.Log("OnAckCallback!");
    }
}

[System.Serializable]
public class JsonData
{
    public string nickName;
    public string chatMessage;
}
