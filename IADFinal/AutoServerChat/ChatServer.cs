using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutoServerChat {
    internal class ChatServer : IDisposable{
        private DateTime startTime;
        private volatile bool stopping = false;
        private UInt64 guid;//id for settling disputes between servers with very close ages

        //list of clients identified by id
        private ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
        private int nextClientId = 0;
        //contains the name and message string from messages to be sent to the clients
        private ConcurrentQueue<Tuple<string, string>> msgQueue = new ConcurrentQueue<Tuple<string, string>>();

        public ChatServer() {
            startTime = DateTime.UtcNow;
            BeginBroadcastHandling();
            BeginAcceptingClients();
            BeginMsgHandling();

            //id for settling disputes between servers with very close ages
            Random rand = new Random();
            guid = (UInt64)(rand.NextDouble() * UInt64.MaxValue);
        }

        public UInt32 GetAge() {
            return (UInt32)(DateTime.UtcNow - startTime).TotalSeconds;
        }

        private void BeginAcceptingClients() {
            Task.Run(() => {
                TcpListener listener = new TcpListener(IPAddress.Any, Protocol.PORT);//todo: exceptions
                try {
                    listener.Start();

                    while(!stopping) {
                        if(listener.Pending()) {
                            TcpClient client = listener.AcceptTcpClient();
                            string name = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                            while(!clients.TryAdd(nextClientId, client)) {
                                nextClientId++;
                            }
                            BeginRecvFromClient(client, name, nextClientId++);
                            msgQueue.Enqueue(new Tuple<string, string>("SERVER", name + " connected."));
                        } else {
                            Task.Delay(50).Wait();
                        }
                    }
                } catch(Exception ex) {
                    if(ex is SocketException) {
                        Stop();
                    } else {
                        throw;
                    }
                } finally {
                    listener.Stop();
                }
            });
        }

        private void BeginMsgHandling() {
            Task.Run(() => {
                while(!stopping) {
                    //todo: exceptions
                    Tuple<string, string> msg = null;//todo: see if this cant be null
                    while(!stopping && msgQueue.TryDequeue(out msg)) {
                        //name
                        byte[] nameBytes = Encoding.Unicode.GetBytes(msg.Item1);
                        byte[] nameLength = { (byte)nameBytes.Length };

                        //message
                        byte[] msgBytes = Encoding.Unicode.GetBytes(msg.Item2);
                        byte[] msgLength = BitConverter.GetBytes((UInt16)msgBytes.Length);

                        //fill buf
                        byte[] buf = new byte[4 + nameBytes.Length + msgBytes.Length];
                        buf[0] = Protocol.SAY_DISPATCH;
                        Buffer.BlockCopy(nameLength, 0, buf, 1, 1);
                        Buffer.BlockCopy(nameBytes, 0, buf, 2, nameBytes.Length);
                        Buffer.BlockCopy(msgLength, 0, buf, 2+nameBytes.Length, 2);
                        Buffer.BlockCopy(msgBytes, 0, buf, 2+nameBytes.Length+2, msgBytes.Length);

                        foreach(var clientKeyValue in clients) {
                            clientKeyValue.Value.GetStream().Write(buf, 0, buf.Length);
                        }
                    }
                    Task.Delay(20).Wait();
                }
            });
        }

        private void BeginRecvFromClient(TcpClient client, string name, int id) {
            Task.Run(() => {
                try {
                    NetworkStream stream = client.GetStream();

                    while(!stopping) {
                        int inByte = -1;
                        if((inByte = stream.ReadByte()) != -1) {
                            if(inByte == Protocol.SAY) {
                                //get message length
                                byte[] inBytes = new byte[2];
                                stream.Read(inBytes, 0, 2);
                                UInt16 msgLen = BitConverter.ToUInt16(inBytes, 0);
                                //get message
                                inBytes = new byte[msgLen];
                                stream.Read(inBytes, 0, msgLen);
                                string msg = Encoding.Unicode.GetString(inBytes);
                                msg = msg.Trim();//todo: trim name aswell

                                //add to queue for dispatching
                                msgQueue.Enqueue(new Tuple<string, string>(name, msg));
                            }else if(inByte == Protocol.SET_NAME) {
                                throw new NotImplementedException();//todo
                            }
                        }
                    }
                } catch(Exception ex) {
                    if(ex is SocketException || ex is IOException) {//todo: others
                        TcpClient ignore;
                        clients.TryRemove(id, out ignore);
                    } else {
                        throw;
                    }
                } finally {
                    client.Close();
                    msgQueue.Enqueue(new Tuple<string, string>("SERVER", name + " disconnected."));
                }
            });
        }

        private void BeginBroadcastHandling() {
            Task.Run(() => {
                using(UdpClient udpClient = new UdpClient()) {//todo: exceptions

                    //set socket to allow multiple multiple binds and broadcasting
                    udpClient.ExclusiveAddressUse = false;
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    udpClient.EnableBroadcast = true;
                    udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.PORT));
                    
                    IPEndPoint destination = new IPEndPoint(IPAddress.Broadcast, Protocol.PORT);

                    while(!stopping) {
                        Task delay;
                        if(GetAge() < 2) {
                            delay = Task.Delay(100);
                        } else {
                            delay = Task.Delay(2000);
                        }

                        //send SERVER_INFO atleast once every loop
                        SendServerInfoPacket(udpClient, destination);

                        //read SERVER_INFO messages
                        while(!delay.IsCompleted) {
                            if(udpClient.Available == 0) {//todo: check that is replying to SERVER_INFO_REQUESTS right away
                                Task.Delay(20).Wait();
                                continue;
                            }

                            IPEndPoint sender = new IPEndPoint(IPAddress.Any, Protocol.PORT);
                            byte[] bytes = udpClient.Receive(ref sender);

                            //reply to info requests
                            if(bytes[0] == Protocol.SERVER_INFO_REQUEST) {
                                SendServerInfoPacket(udpClient, destination);
                            }
                            //check if SERVER_INFO
                            else if(bytes[0] == Protocol.SERVER_INFO && bytes.Length == 17) {
                                UInt32 thisAge = GetAge();//get age right away

                                //check if own broadcast before CRC to prevent wasting resources
                                UInt64 otherGuid = BitConverter.ToUInt64(bytes, 5);
                                if(otherGuid == guid) {
                                    continue;//skip
                                }

                                //todo: check crc 

                                UInt32 otherAge = BitConverter.ToUInt32(bytes, 1);

                                //if other server is more legitimate
                                if(((Int64)otherAge - thisAge > 2) || 
                                    (Math.Abs((Int64)otherAge - thisAge) <= 2 && otherGuid > guid)) {
                                    Stop();//signal server to stop
                                }

                            }                      
                        }

                    }
                }
            });
        }

        private void SendServerInfoPacket(UdpClient client, IPEndPoint dest) {
            byte[] buf = new byte[17];

            buf[0] = Protocol.SERVER_INFO;
            Buffer.BlockCopy(BitConverter.GetBytes(guid), 0, buf, 5, 8);
            //todo: crc (https://msdn.microsoft.com/en-ca/library/ee431960.aspx)
            Buffer.BlockCopy(BitConverter.GetBytes(GetAge()), 0, buf, 1, 4);

            client.Send(buf, buf.Length, dest);
        }

        private void Stop() {
            stopping = true;
            foreach(var client in clients) {
                client.Value.Close();
            }    
        }

        #region IDisposable Support
        private bool disposed = false; // To detect redundant calls

        //todo: make sure all tasks are stopped BEFORE dispose returns
        public void Dispose() {
            if(!disposed) {
                Stop();
                disposed = true;
            }
        }
        #endregion
    }
}
