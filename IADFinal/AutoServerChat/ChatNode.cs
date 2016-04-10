using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace AutoServerChat {
    public class ChatNode : IChatNode {
        public event EventHandler<MessageSaidEventArgs> MessageSaid;

        private object clientLock = new object();
        private ChatClient client;//only access when locking clientLock

        private ChatServer server;
        private volatile bool serverRunning = false;
        private ConcurrentQueue<string> sayBacklog = new ConcurrentQueue<string>();

        public string Name {
            get {
                throw new NotImplementedException();
            }
            set {
                throw new NotImplementedException();
            }
        }

        public ChatNode() {
            client = new ChatClient();
            client.ConnectionLost += ClientConnectionLost;
            client.MessageSaid += (s, e) => {
                if(MessageSaid != null) {
                    MessageSaid(s, e);
                }
            };
        }

        private void ClientConnectionLost(object sender, EventArgs e) {
            MessageSaid(this, new MessageSaidEventArgs("CLIENT", "Connection Lost."));
            StartSessionTask();
        }

        public void Start() {
            StartSessionTask();
            //server = new ChatServer();//todo: switch to test, then remove
            //serverRunning = true;
        }

        //todo: exceptions
        private Task StartSessionTask() {
            if(MessageSaid != null) {
                MessageSaid(this, new MessageSaidEventArgs("CLIENT", "Searching for session..."));
            }
            return Task.Run(() => {
                while(!client.Connected) {
                    List<CandidateServer> candidates = GetCandidateServers();

                    lock (clientLock) {
                        if(client != null) {
                            client.Close();
                        }

                        foreach(var server in candidates) {
                            try {
                                client.Connect(new IPEndPoint(server.Ip, Protocol.PORT));
                                break;
                            } catch(Exception ex) {
                                //todo: only catch expected exceptions
                            }
                        }

                        //if can't connect to anything, start new server and connect to that
                        if(!client.Connected) {
                            if(MessageSaid != null) {
                                MessageSaid(this, new MessageSaidEventArgs("CLIENT", "Starting new session."));
                            }
                            //todo: make sure this is a good check if the server is running
                            if(server != null) {
                                server.Dispose();
                            }
                            try {
                                server = new ChatServer();
                                client.Connect(new IPEndPoint(IPAddress.Parse("127.0.0.1"), Protocol.PORT));//todo: exceptions
                            } catch(Exception ex) {
                                //todo: only catch expected exceptions
                            }
                        }

                    }
                }

                if(MessageSaid != null) {
                    MessageSaid(this, new MessageSaidEventArgs("CLIENT", "Connected."));
                }

                //write backlog
                string msg = "";
                while(sayBacklog.TryDequeue(out msg)) {
                    client.Say(msg);//todo: exceptions
                }
            });
        }

        //candidates are sorted from oldest to newest.
        private List<CandidateServer> GetCandidateServers() {
            List<CandidateServer> candidates = new List<CandidateServer>();

            using(UdpClient udpClient = new UdpClient()) {//todo: exception


                //set socket to allow multiple multiple binds and broadcasting
                udpClient.ExclusiveAddressUse = false;
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                udpClient.EnableBroadcast = true;

                udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, Protocol.PORT));

                IPEndPoint remoteEnd = new IPEndPoint(IPAddress.Broadcast, Protocol.PORT);

                DateTime stopTime = DateTime.UtcNow.AddSeconds(2);
                while(DateTime.UtcNow < stopTime) {
                    Task delay = Task.Delay(100);
                    
                    //request server info
                    udpClient.Send(new byte[] { Protocol.SERVER_INFO_REQUEST }, 1, remoteEnd);

                    //read SERVER_INFO messages
                    while(udpClient.Available > 0) {
                        IPEndPoint sender = new IPEndPoint(IPAddress.Any, Protocol.PORT);
                        byte[] bytes = udpClient.Receive(ref sender);
                        
                        //check if SERVER_INFO
                        if(bytes.Length != 17 || bytes[0] != Protocol.SERVER_INFO) {
                            continue;//skip this one
                        }

                        //todo: check crc 

                        IPAddress ip = sender.Address;
                        UInt64 guid = BitConverter.ToUInt64(bytes, 5);
                        UInt32 age = BitConverter.ToUInt32(bytes, 1);

                        //check if allready in list
                        bool skip = false;
                        foreach(var server in candidates) {
                            if(server.Ip.GetAddressBytes().SequenceEqual(ip.GetAddressBytes())) {
                                skip = true;//skip this one
                                break;
                            }
                        }
                        if(skip) {
                            continue;
                        }
                        
                        //add to list of candidates
                        candidates.Add(new CandidateServer(ip, age, guid));

                        //set remaining time to max of one second
                        if(stopTime > DateTime.UtcNow.AddSeconds(1)) {//more that 1 second left
                            stopTime = DateTime.UtcNow.AddSeconds(1);
                        }
                    }

                    delay.Wait();
                }
            }

            candidates.Sort();//todo: CHECK THIS
            return candidates;
        }

        public void Say(string msg) {
            if(client.Connected) {
                client.Say(msg);//todo: exceptions
            } else {
                sayBacklog.Enqueue(msg);
            }
        }

        private class CandidateServer : IComparable<CandidateServer> {
            public readonly IPAddress Ip;
            public readonly UInt32 Age;//at the time added
            public readonly DateTime TimeAdded;
            public readonly UInt64 Guid;

            public CandidateServer(IPAddress ip, UInt32 age, UInt64 guid) {
                Ip = ip;
                Age = age;
                Guid = guid;
                TimeAdded = DateTime.UtcNow;
            }

            /**
             * @fn  public int CompareTo(CandidateServer other)
             *
             * @brief   Compares this CandidateServer object to another to determine their relative ordering.
             *
             * @param   other   The object to compare with this object.
             *
             * @return  Greater than 0 if greater than other, Less than 0 if less than other.   
             */
            public int CompareTo(CandidateServer other) {
                double thisAge = this.Age + (DateTime.UtcNow - this.TimeAdded).TotalSeconds;
                double otherAge = other.Age + (DateTime.UtcNow - other.TimeAdded).TotalSeconds;
                if(Math.Abs(thisAge - otherAge) < 2) {
                    //compare ip if age difference is with margin of error
                    if(this.Guid > other.Guid) {
                        return 1;
                    } else if(this.Guid == other.Guid) {
                        return 0;
                    } else {
                        return -1;
                    }
                } else {
                    return Math.Sign(otherAge - otherAge);
                }
            }
        }
    }
}
