var io = require('socket.io')(8889);
console.log('Server Start....');
 
var socketArray = {};//Socket
 
io.on('connection', function (socket) {
    console.log('Client Contect. SocketID:');
	socket.emit('message', "MyNick", "Msg to the client");
	
    socket.emit('Info', { hi: 'ConnectServer Scuess!' });	
	socket.on('message',function (data,data2){
		  console.log(data);
          console.log(data2);			  
	});
	
	
	socket.on('custom event',function (data,data2){
		  console.log(data);
          console.log(data2);		  
	});
	
    //socket.emit('Info', "ConnectServer Scuess! :" + socket.name);
    socket.on('Login', function (data) {
        console.log(data);
        //var content = JSON.parse(data);
        socket.nickName = data.nickName;
        socket.guid = data.guid;
        //
        if (!socketArray.hasOwnProperty(socket.guid)) {
            socketArray[socket.guid] = data;
        }
 
        //
        //console.log(socketArray);
        var chatContent = {};
        chatContent.nickName = socket.nickName;
        chatContent.chatMessage = "Login Scuess";
 
        socket.emit('Login', chatContent);//把登陆消息转给客户端
 
    });
 
    socket.on('Chat', function (data) {
        //console.log("SocketProtocol.Chat:"+data);
 
        var chatContent = {};
        chatContent.nickName = socket.nickName;
        chatContent.chatMessage = data.chatMessage;
 
        console.log("chatContent.toJSON():" + JSON.stringify(chatContent));
        io.sockets.emit('Chat', chatContent);//All SocketUsers  把聊天消息转给客户端
        //socket.broadcast.emit(data);//All SocketUsers But Self
    });
 
    socket.on('disconnect', function () {
        console.log("disconnect");
        if (socketArray.hasOwnProperty(socket.guid)) {
            delete socketArray[socket.guid];
            console.log("disconnect Delete:" + socket.guid);
        }
        console.log("Inline Count:" + countProperties(socketArray));
    });
 
});
 
function countProperties(obj) {
    var count = 0;
    for (var property in obj) {
        if (Object.prototype.hasOwnProperty.call(obj, property)) {
            count++;
        }
    }
    return count;
}
 
this.size = function () {
    var count = 0;
    for (var prop in items) {
        if (items.hasOwnProperty(prop)) {
            ++count;
        }
    }
    return count;
};