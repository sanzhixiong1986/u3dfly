using System;
using System.Collections;
using BestHTTP;
using BestHTTP.SocketIO;
using System.Collections.Generic;
using UnityEditor.UI;

/**
 * socket使用类
 */
public class NetWork
{
    private Socket socket = null;                                       //socket对象
    private const String URl = "http://localhost:8989/socket.io/";      //socket的url对象
    private static NetWork _inter = null;

    //单利模式
    public static NetWork GetNetWork(){
        if(_inter == null){
            _inter = new NetWork();
        }
        return _inter;
    }

    public void init()
    {
        var manager = new SocketManager(new System.Uri(URl));
        socket = manager.Socket;
        OnAddEvent();
    }

    //增加事件
    private void OnAddEvent()
    {
        socket.Once("connect", OnConnected);    //连接
        socket.On("Info", OnInfo);              //
        socket.On("message", OnMessage);        //获得消息
    }

    //回掉事件
    private void OnConnected(Socket socket,Packet packet,params object[] args)
    {
        NGUIDebug.Log("连接上了");
    }

    //初始化
    private void OnInfo(Socket socket, Packet packet, params object[] args)
    {
        NGUIDebug.Log(packet.ToString());
        NGUIDebug.Log(args.ToString());
    }

    //返回对象操作
    private void OnMessage(Socket socket, Packet packet, params object[] args)
    {
        // args[0] is the nick of the sender 
        // args[1] is the message 
        NGUIDebug.Log(string.Format("Message from {0}: {1}", args[0], args[1]));
    }

    //发送事件
    public void sendMesssage(params object[] args)
    {
        socket.Emit("message",args);
    }
}