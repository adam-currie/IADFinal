/**
* @file		ChatClient.cs
* @project	AutoServerChat
* @author	Adam Currie & Alexander Martin
* @date		2016-04-7
 */
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AutoServerChat {
    internal class ChatClient : IChatNode {
        public event EventHandler<MessageSaidEventArgs> MessageSaid;
        public event EventHandler ConnectionLost;//add handler
        private TcpClient client;
        private int clientAccessCounter = 0;//number of execution context's accessing the tcp client
        private volatile bool connected = false;
        private string name = null;
        private readonly object nameLock = new object(); 

        //threadsafe
        public bool Connected {
            get {
                return connected;
            }
        }

        /**
         * @property    public string Name
         *
         * @brief   Gets or sets the name that identifies messages from this node.
         *
         * @exception   ArgumentException   Thrown when name is empty or too long.
         *
         * @return  The name or null if unset.
         */
        public string Name {
            get {
                lock (nameLock){
                    return name;
                }
            }
            set {
                value = value.Trim();
                //check empty
                if(value == "") {
                    throw new ArgumentException("name cannot be empty.");
                }

                //check byte len
                if(Encoding.Unicode.GetBytes(value).Length > byte.MaxValue) {
                    throw new ArgumentException("message length is too long.");
                }

                lock (nameLock) {
                    name = value;
                    if(connected) {
                        SendNameTask();
                    }
                }
            }
        }

        /**
         * @fn  public void Connect(IPEndPoint server)
         *
         * @brief   Connects to the specified host.
         *
         * @exception   InvalidOperationException   Thrown when already connected.
         * @exception   SocketException             Thrown when a Socket error condition occurs.
         *
         * @param   server  The port.
         */
        public void Connect(IPEndPoint server) {
            if(connected) {
                throw new InvalidOperationException("already connected");
            }

            client = new TcpClient();
            client.ExclusiveAddressUse = false;
            client.Connect(server);
            BeginRecv();

            SendNameTask();
            connected = true;
        }

        private void BeginRecv() {
            Task.Run(() => {
                Interlocked.Increment(ref clientAccessCounter);
                try {
                    NetworkStream stream = client.GetStream();
                    while(connected) {
                        int inByte = -1;
                        if((inByte = stream.ReadByte()) != -1){
                            if(inByte == Protocol.SAY_DISPATCH) {

                                //get name length
                                byte[] inBytes = new byte[1];
                                stream.Read(inBytes, 0, 1);
                                byte nameLen = inBytes[0];
                                //get name
                                inBytes = new byte[nameLen];
                                stream.Read(inBytes, 0, nameLen);
                                string sayer = Encoding.Unicode.GetString(inBytes);

                                //get message length
                                inBytes = new byte[2];
                                stream.Read(inBytes, 0, 2);
                                UInt16 msgLen = BitConverter.ToUInt16(inBytes, 0);
                                //get message
                                inBytes = new byte[msgLen];
                                stream.Read(inBytes, 0, msgLen);
                                string msg = Encoding.Unicode.GetString(inBytes);

                                if(MessageSaid != null) {
                                    MessageSaid(this, new MessageSaidEventArgs(sayer, msg));
                                }
                            }
                        } else {
                            //small delay before checking again for something to read
                            Task.Delay(50).Wait();
                        }
                    }
                } catch(Exception ex) {
                    if(ex is SocketException || ex is IOException || ex is ObjectDisposedException) {
                        connected = false;
                        if(ConnectionLost != null) {
                            ConnectionLost(this, null);
                        }
                    } else {
                        throw;
                    }
                } finally {
                    Interlocked.Decrement(ref clientAccessCounter);
                    Close();
                }
            });
        }

        private Task SendNameTask() {
            return Task.Run(() => {
                if(!connected) {
                    return;
                }

                byte[] nameBytes;

                lock (nameLock) {
                    if(name == null) {
                        return;
                    }
                    //get msg bytes
                    nameBytes = Encoding.Unicode.GetBytes(name);
                }

                //get length bytes
                byte[] lengthByte = BitConverter.GetBytes((byte)nameBytes.Length);

                //fill buffer
                byte[] buf = new byte[2 + nameBytes.Length];
                buf[0] = Protocol.SET_NAME;
                buf[1] = lengthByte[0];
                Buffer.BlockCopy(nameBytes, 0, buf, 2, nameBytes.Length);

                Interlocked.Increment(ref clientAccessCounter);
                try {
                    client.GetStream().Write(buf, 0, buf.Length);
                } catch(Exception ex) {
                    if(ex is IOException || ex is ObjectDisposedException) {
                        //todo: handle connection loss
                    } else {
                        throw;
                    }
                } finally {
                    Interlocked.Decrement(ref clientAccessCounter);
                }
            });
        }

        public void Say(string msg) {
            msg = msg.Trim();//to save bandwith, trimmed on server side anyway.

            //check empty
            if(msg == "") {
                throw new ArgumentException("message is empty.");
            }

            //get msg bytes
            byte[] msgBytes = Encoding.Unicode.GetBytes(msg);
            if(msgBytes.Length > UInt16.MaxValue) {
                throw new ArgumentException("message length is too long.");
            }

            Task.Run(() => {
                if(!connected) {
                    return;
                }

                //get length bytes
                byte[] lengthBytes = BitConverter.GetBytes((UInt16)msgBytes.Length);

                //fill buffer
                byte[] buf = new byte[3 + msgBytes.Length];
                buf[0] = Protocol.SAY;
                Buffer.BlockCopy(lengthBytes, 0, buf, 1, 2);
                Buffer.BlockCopy(msgBytes, 0, buf, 3, msgBytes.Length);

                Interlocked.Increment(ref clientAccessCounter);
                try {
                    client.GetStream().Write(buf, 0, buf.Length);
                } catch(Exception ex) {
                    if(ex is IOException || ex is ObjectDisposedException) {
                        //todo: handle connection loss
                    } else {
                        throw;
                    }
                } finally {
                    Interlocked.Decrement(ref clientAccessCounter);
                }
            });
        }

        public void Close() {
            if(connected) {
                connected = false;//signal everything to stop

                //wait for threads using client to wrap up
                while(clientAccessCounter > 0) {
                    Task.Delay(10).Wait();
                }

                if(client != null) {
                    client.Close();
                }
                connected = false;
            }
        }
    }
}
