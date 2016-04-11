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

    /**
     * @class   ChatClient
     *
     * @brief   Client used to connect to chat session.
     */
    internal class ChatClient : IChatNode {
        public event EventHandler<MessageSaidEventArgs> MessageSaid;
        public event EventHandler ConnectionLost;

        private int clientAccessCounter = 0;//number of execution context's accessing the tcp client
        private volatile bool connected = false;
        private TcpClient client;
        private readonly object clientWriteLock = new object();//locked when writing to client

        private string name = null;
        private readonly object nameLock = new object(); 

        /**
         * @property    public bool Connected
         *
         * @brief   Connection status, threadsafe access.
         *
         * @return  true if connected, false if not.
         */
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
         * @exception   SocketException             Thrown when an underlying socket error condition occurs.
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

            //only reached if connected
            SendNameTask();
            connected = true;
        }

        /**
         * @fn  private void BeginRecv()
         *
         * @brief   Begins receiving messages from the server.
         */
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
                    if(ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException) {
                        //end connection
                    } else {
                        throw;
                    }
                } finally {
                    Interlocked.Decrement(ref clientAccessCounter);
                    Stop();
                }
            });
        }

        /**
         * @fn  private Task SendNameTask()
         *
         * @brief   Sends the name to the server.
         *
         * @return  The Task.
         */
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
                    lock (clientWriteLock) {
                        client.GetStream().Write(buf, 0, buf.Length);
                    }
                } catch(Exception ex) {
                    if(ex is SocketException || ex is ObjectDisposedException) {
                        //end connection
                        Stop();
                    } else {
                        throw;
                    }
                } finally {
                    Interlocked.Decrement(ref clientAccessCounter);
                }
            });
        }

        /**
         * @fn  public void Say(string msg)
         *
         * @brief   Sends a say message to the server.
         *
         * @exception   ArgumentException   Thrown when message is empty or too long (check the exception message).
         *
         * @param   msg The message.
         */
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
                    lock (clientWriteLock) {
                        client.GetStream().Write(buf, 0, buf.Length);
                    }
                } catch(Exception ex) {
                    if(ex is SocketException || ex is ObjectDisposedException) {
                        //end connection
                        Stop();
                    } else {
                        throw;
                    }
                } finally {
                    Interlocked.Decrement(ref clientAccessCounter);
                }
            });
        }

        /**
         * @fn  public void Close()
         *
         * @brief   Closes the connection and waits for everything to wrap up.
         */
        public void Close() {
            Stop(false);
        }

        /**
         * @fn  private Task Stop()
         *
         * @brief   Stops the connection.
         *
         * @return The Task.
         */
        private Task Stop(bool raiseEvent = true) {
            return Task.Run(() => {

                if(raiseEvent) {
                    if(ConnectionLost != null) {
                        ConnectionLost(this, null);
                    }
                }

                connected = false;//signal everything to stop

                while(clientAccessCounter > 0) {
                    Task.Delay(10).Wait();
                }

                if(client != null) {
                    client.Close();
                }
            });
        }
    }
}