using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        public string Name {
            get {
                throw new NotImplementedException();
            }
            set {
                if(!connected) {
                    throw new InvalidOperationException("not connected");
                }
                throw new NotImplementedException();
            }
        }

        public bool Connected {
            get {
                return connected;
            }
        }

        /**
         * @fn  public Client(string host, int port)
         *
         * @brief   Constructer, connects to the specified host.
         *
         * @param   port    The port.
         */
        public void Connect(IPEndPoint server) {
            if(connected) {
                throw new InvalidOperationException("already connected");
            }

            //todo: exceptions
            client = new TcpClient();
            client.ExclusiveAddressUse = false;
            client.Connect(server);
            BeginRecv();
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
                                stream.Read(inBytes, 0, nameLen);//todo: make sure this is reading it all
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

        public void Say(string msg) {
            //todo: maybe do all on new task
            if(!connected) {
                throw new InvalidOperationException("not connected");
            }

            msg = msg.Trim();//to save bandwith, trimmed on server side anyway.

            //get msg bytes
            byte[] msgBytes = Encoding.Unicode.GetBytes(msg);
            if(msgBytes.Length > UInt16.MaxValue) {
                throw new ArgumentException("message length is too long.");
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
