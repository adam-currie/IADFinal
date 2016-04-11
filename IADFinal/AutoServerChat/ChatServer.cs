/**
* @file		ChatServer.cs
* @project	AutoServerChat
* @author	Adam Currie & Alexander Martin
* @date		2016-04-7
 */
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutoServerChat {

    /**
     * @class   ChatServer
     *
     * @brief   Standalone multi-threaded chat server.
     */
    internal class ChatServer : IDisposable{
        private DateTime startTime;
        private volatile bool stopping = false;
        private UInt64 uid;//id for settling disputes between servers with very close ages

        //list of clients identified by id
        private ConcurrentDictionary<int, TcpClient> clients = new ConcurrentDictionary<int, TcpClient>();
        private int nextClientId = 0;

        //contains the name and message string for messages to be sent to clients
        private ConcurrentQueue<Tuple<string, string>> msgQueue = new ConcurrentQueue<Tuple<string, string>>();
        private ConcurrentBag<Task> tasks = new ConcurrentBag<Task>();

        /**
         * @fn  public ChatServer()
         *
         * @brief   Default constructor, starts running the server.
         */
        public ChatServer() {
            startTime = DateTime.UtcNow;

            tasks.Add(BroadcastHandlingTask());
            tasks.Add(AcceptingClientsTask());
            tasks.Add(MsgHandlingTask());

            //id for settling disputes between servers with very close ages
            Random rand = new Random();
            uid = (UInt64)(rand.NextDouble() * UInt64.MaxValue);
        }

        /**
         * @fn  public UInt32 GetAge()
         *
         * @brief   Gets the age of the server.
         *
         * @return  The age.
         */
        public UInt32 GetAge() {
            return (UInt32)(DateTime.UtcNow - startTime).TotalSeconds;
        }

        /**
         * @fn  private Task AcceptingClientsTask()
         *
         * @brief   Accepts new tcp clients.
         *
         * @return  The task.
         */
        private Task AcceptingClientsTask() {
            return Task.Run(() => {
                TcpListener listener = new TcpListener(IPAddress.Any, Protocol.PORT);
                try {
                    listener.Start();
                    
                    while(!stopping) {
                        if(listener.Pending()) {
                            TcpClient client = listener.AcceptTcpClient();

                            //default name is client's IP address
                            string name = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                            while(!clients.TryAdd(nextClientId, client)) {
                                nextClientId++;
                            }
                            tasks.Add(RecvFromClientTask(client, name, nextClientId++));

                            //notify clients
                            msgQueue.Enqueue(new Tuple<string, string>("SERVER", name + " connected."));
                        } else {
                            Task.Delay(50).Wait();
                        }
                    }
                } catch(SocketException) {
                    Stop();//shut down server
                } finally {
                    listener.Stop();
                }
            });
        }

        /**
         * @fn  private Task MsgHandlingTask()
         *
         * @brief   Dispatches messages to the clients.
         *
         * @return  The Task.
         */
        private Task MsgHandlingTask() {
            return Task.Run(() => {
                while(!stopping) {
                    Tuple<string, string> msg = null;
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
                            try {
                                clientKeyValue.Value.GetStream().Write(buf, 0, buf.Length);
                            }catch(Exception ex) {
                                if(ex is SocketException || ex is ObjectDisposedException || ex is InvalidOperationException) {
                                    TcpClient client;
                                    if(clients.TryRemove(clientKeyValue.Key, out client)) {
                                        client.Close();//RecvFromClient will send add disconnect msg
                                    }
                                }
                            }
                        }
                    }
                    Task.Delay(20).Wait();
                }
            });
        }

        /**
         * @fn  private Task RecvFromClientTask(TcpClient client, string name, int id)
         *
         * @brief   Receives messages from a client.
         *
         * @param   client  The client.
         * @param   name    The name of the client.
         * @param   id      The id of the client.
         *
         * @return  The Task.
         */
        private Task RecvFromClientTask(TcpClient client, string name, int id) {
            return Task.Run(() => {
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
                                msg = msg.Trim();

                                //add to queue for dispatching
                                msgQueue.Enqueue(new Tuple<string, string>(name, msg));
                            }else if(inByte == Protocol.SET_NAME) {
                                //get name length
                                byte nameLen = (byte)stream.ReadByte();
                                //get message
                                byte[] inBytes = new byte[nameLen];
                                stream.Read(inBytes, 0, nameLen);
                                string newName = Encoding.Unicode.GetString(inBytes);
                                newName = newName.Trim();

                                if(name == newName) {
                                    continue;//skip
                                }

                                //add to queue for dispatching
                                msgQueue.Enqueue(new Tuple<string, string>("SERVER", name + " changed their name to " + newName));

                                name = newName;
                            }
                        }
                    }
                } catch(Exception ex) {
                    if(ex is SocketException || ex is IOException || ex is ObjectDisposedException) {
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

        /**
         * @fn  private Task BroadcastHandlingTask()
         *
         * @brief   Handles the udp broadcasting, sends server info to clients and resolves best host with other hosts.
         *
         * @return  The Task.
         */
        private Task BroadcastHandlingTask() {
            return Task.Run(() => {
                var crcGen = new DamienG.Security.Cryptography.Crc32();
                UdpClient udpClient = new UdpClient();

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
                        if(udpClient.Available == 0) {
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
                            UInt64 otherUid = BitConverter.ToUInt64(bytes, 5);
                            if(otherUid == uid) {
                                continue;//skip
                            }

                            //check crc
                            byte[] crc = new byte[4];
                            Buffer.BlockCopy(bytes, 13, crc, 0, 4);
                            if(crcGen.ComputeHash(bytes, 1, 12).SequenceEqual(bytes)) {
                                continue;//skip
                            }

                            UInt32 otherAge = BitConverter.ToUInt32(bytes, 1);

                            //if other server is more legitimate
                            if(((Int64)otherAge - thisAge > 2) || 
                                (Math.Abs((Int64)otherAge - thisAge) <= 2 && otherUid > uid)) {
                                Stop();//signal server to stop
                            }

                        }                      
                    }
                }

                udpClient.Close();

            });
        }

        /**
         * @fn  private void SendServerInfoPacket(UdpClient client, IPEndPoint dest)
         *
         * @brief   Sends a server information packet.         
         *
         * @param   client  The client.
         * @param   dest    Destination for the.
         */
        private void SendServerInfoPacket(UdpClient client, IPEndPoint dest) {
            var crcGen = new DamienG.Security.Cryptography.Crc32();
            byte[] buf = new byte[17];

            //fill packet
            buf[0] = Protocol.SERVER_INFO;
            Buffer.BlockCopy(BitConverter.GetBytes(GetAge()), 0, buf, 1, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(uid), 0, buf, 5, 8);

            //crc
            byte[] crc = crcGen.ComputeHash(buf, 1, 12);
            Buffer.BlockCopy(crc, 0, buf, 13, 4);

            //send
            try {
                client.Send(buf, buf.Length, dest);
            } catch(SocketException) {}
        }

        /**
         * @fn  private void Stop()
         *
         * @brief   Stops the server and all it's tasks.
         */
        private void Stop() {
            stopping = true;
            foreach(var client in clients) {
                client.Value.Close();
            }
        }

        #region IDisposable Support
        private bool disposed = false; // To detect redundant calls

        /**
         * @fn  public void Dispose()
         *
         * @brief   Stops server and, and waits for completion.
         */
        public void Dispose() {
            if(!disposed) {
                Stop();

                //wait for threads
                while(!tasks.IsEmpty) {
                    Task t;
                    tasks.TryTake(out t);
                    t.Wait();
                }

                disposed = true;
            }
        }
        #endregion
    }
}
