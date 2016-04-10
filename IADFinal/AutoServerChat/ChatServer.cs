using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutoServerChat {
    internal class ChatServer : IDisposable{
        private DateTime startTime;
        private volatile bool stopping = false;
        private UInt64 guid = 0;//todo: generate
        private ConcurrentBag<TcpClient> clients = new ConcurrentBag<TcpClient>();
        //contains the name and message string from messages to be sent to the clients
        private ConcurrentQueue<Tuple<string, string>> msgQueue = new ConcurrentQueue<Tuple<string, string>>();

        public ChatServer() {
            startTime = DateTime.UtcNow;
            BeginBroadcastHandling();
            BeginAcceptingClients();
            BeginMsgHandling();
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
                            BeginHandleClient(client);
                            clients.Add(client);
                        } else {
                            Task.Delay(50).Wait();
                        }
                    }
                } catch(Exception ex) {
                    throw;//todo: handle exceptions
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
                        buf[0] = Protocol.SAY;
                        Buffer.BlockCopy(nameLength, 0, buf, 1, 1);
                        Buffer.BlockCopy(nameBytes, 0, buf, 2, nameBytes.Length);
                        Buffer.BlockCopy(msgLength, 0, buf, 2+nameBytes.Length, 2);
                        Buffer.BlockCopy(msgBytes, 0, buf, 2+nameBytes.Length+2, msgBytes.Length);

                        foreach(TcpClient client in clients) {
                            client.GetStream().Write(buf, 0, buf.Length);
                        }
                    }
                    Task.Delay(20).Wait();
                }
            });
        }
        private void BeginHandleClient(TcpClient client) {
            Task.Run(() => {
                try {
                    while(!stopping) {
                        //todo: receive from and add messages to msgQueue
                    }
                } catch(Exception ex) {
                    throw;//todo: handle exceptions
                } finally {
                    client.Close();
                }
            });
        }

        private void BeginBroadcastHandling() {
            Task.Run(() => {
                using(UdpClient udpClient = new UdpClient()) {//todo: exceptions

                    //todo: remove next 2 lines and see if 2 clients can still coexiston on one pc
                    udpClient.ExclusiveAddressUse = false;
                    //debug: maybe enable below
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    //todo: check if i need to set SocketOptionName.Broadcast
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

                        //read SERVER_INFO messages
                        while(!delay.IsCompleted) {
                            if(udpClient.Available == 0) {
                                Task.Delay(20).Wait();
                                continue;
                            }

                            IPEndPoint sender = new IPEndPoint(IPAddress.Any, Protocol.PORT);
                            byte[] bytes = udpClient.Receive(ref sender);
                            sender.Address = IPAddress.Any;

                            //reply to info requests
                            if(bytes[0] == Protocol.SERVER_INFO_REQUEST) {
                                SendServerInfoPacket(udpClient, destination);
                            }
                            //check if SERVER_INFO
                            else if(bytes[0] == Protocol.SERVER_INFO && bytes.Length == 13) {
                                UInt32 thisAge = GetAge();

                                //todo: check crc 

                                IPAddress otherIp = sender.Address;
                                UInt64 otherGuid = BitConverter.ToUInt64(bytes, 5);
                                UInt32 otherAge = BitConverter.ToUInt32(bytes, 1);

                                //if other server is more legitimate
                                if(((Int64)otherAge - thisAge > 2) || 
                                    (Math.Abs((Int64)otherAge - thisAge) <= 2 && otherGuid > guid)) {
                                    stopping = true;//signal server to stop
                                }

                            }                      
                        }

                        //send SERVER_INFO atleast once every loop
                        SendServerInfoPacket(udpClient, destination);
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

        #region IDisposable Support
        private bool disposed = false; // To detect redundant calls

        //todo: make sure all tasks are stopped BEFORE dispose returns
        public void Dispose() {
            if(!disposed) {
                stopping = true;//signal everything to stop

                //todo: wait for tasks to stop                

                //todo: close stuff
                disposed = true;
            }
        }
        #endregion
    }
}
