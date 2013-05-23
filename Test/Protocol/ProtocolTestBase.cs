﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using SuperSocket.SocketBase;
using SuperSocket.SocketBase.Config;
using SuperSocket.SocketBase.Logging;
using SuperSocket.SocketBase.Protocol;

namespace SuperSocket.Test.Protocol
{
    public abstract class ProtocolTestBase
    {
        private TestServer m_Server;

        protected abstract IReceiveFilterFactory<StringRequestInfo> CurrentReceiveFilterFactory { get; }

        [SetUp]
        public void Setup()
        {
            var mode = TestContext.CurrentContext.Test.Name.Contains("Udp") ? SocketMode.Udp : SocketMode.Tcp;
            m_Server = CreateServer(CurrentReceiveFilterFactory, mode);
        }

        [TearDown]
        public void TearDown()
        {
            m_Server.Stop();
        }

        private TestServer CreateServer(IReceiveFilterFactory<StringRequestInfo> receiveFilterFactory)
        {
            return CreateServer(receiveFilterFactory, SocketMode.Tcp);
        }

        private TestServer CreateServer(IReceiveFilterFactory<StringRequestInfo> receiveFilterFactory, SocketMode mode)
        {
            var appServer = new TestServer();

            var serverConfig = new ServerConfig();
            serverConfig.Ip = "127.0.0.1";
            serverConfig.Port = 2012;
            serverConfig.Mode = mode;
            serverConfig.DisableSessionSnapshot = true;

            var setupResult = appServer.Setup(serverConfig, null, receiveFilterFactory, new ConsoleLogFactory(), null, null);

            Assert.IsTrue(setupResult);
            Assert.IsTrue(appServer.Start());

            return appServer;
        }

        protected Socket CreateClient()
        {
            var serverAddress = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 2012);
            var socket = new Socket(serverAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            socket.Connect(serverAddress);

            return socket;
        }

        protected abstract string CreateRequest(string sourceLine);

        [Test]
        public void TestNormalRequest()
        {
            using (var socket = CreateClient())
            {
                var socketStream = new NetworkStream(socket);
                using (var reader = new StreamReader(socketStream, Encoding.ASCII, true))
                using (var writer = new ConsoleWriter(socketStream, Encoding.ASCII, 1024 * 8))
                {
                    reader.ReadLine();

                    var line = Guid.NewGuid().ToString();
                    writer.Write(CreateRequest(line));
                    writer.Flush();

                    var receivedLine = reader.ReadLine();

                    Assert.AreEqual(line, receivedLine);
                }
            }
        }

        [Test]
        public void TestFragmentRequest()
        {
            using (var socket = CreateClient())
            {
                var socketStream = new NetworkStream(socket);
                using (var reader = new StreamReader(socketStream, Encoding.ASCII, true))
                using (var writer = new ConsoleWriter(socketStream, Encoding.ASCII, 1024 * 8))
                {
                    reader.ReadLine();

                    var line = Guid.NewGuid().ToString();
                    var request = CreateRequest(line);

                    for (var i = 0; i < request.Length; i++)
                    {
                        writer.Write(request[i]);
                        writer.Flush();
                        Thread.Sleep(50);
                    }

                    var receivedLine = reader.ReadLine();
                    Assert.AreEqual(line, receivedLine);
                }
            }
        }

        [Test]
        public void TestBatchRequest()
        {
            using (var socket = CreateClient())
            {
                var socketStream = new NetworkStream(socket);
                using (var reader = new StreamReader(socketStream, Encoding.ASCII, true))
                using (var writer = new ConsoleWriter(socketStream, Encoding.ASCII, 1024 * 8))
                {
                    reader.ReadLine();

                    int size = 100;

                    var lines = new string[size];

                    for (var i = 0; i < size; i++)
                    {
                        var line = Guid.NewGuid().ToString();
                        lines[i] = line;
                        var request = CreateRequest(line);
                        writer.Write(request);
                    }

                    writer.Flush();

                    for (var i = 0; i < size; i++)
                    {
                        var receivedLine = reader.ReadLine();
                        Assert.AreEqual(lines[i], receivedLine);
                    }
                }
            }
        }

        [Test]
        public void TestBreakRequest()
        {
            using (var socket = CreateClient())
            {
                var socketStream = new NetworkStream(socket);
                using (var reader = new StreamReader(socketStream, Encoding.ASCII, true))
                using (var writer = new ConsoleWriter(socketStream, Encoding.ASCII, 1024 * 8))
                {
                    reader.ReadLine();

                    int size = 1000;

                    var lines = new string[size];

                    var sb = new StringBuilder();

                    for (var i = 0; i < size; i++)
                    {
                        var line = Guid.NewGuid().ToString();
                        lines[i] = line;
                        sb.Append(CreateRequest(line));
                    }

                    var source = sb.ToString();

                    var rd = new Random();

                    var rounds = new List<KeyValuePair<int, int>>();

                    var rest = source.Length;

                    while (rest > 0)
                    {
                        if (rest == 1)
                        {
                            rounds.Add(new KeyValuePair<int, int>(source.Length - rest, 1));
                            rest = 0;
                            break;
                        }

                        var thisRound = rd.Next(1, rest);
                        rounds.Add(new KeyValuePair<int, int>(source.Length - rest, thisRound));
                        rest -= thisRound;
                    }

                    for (var i = 0; i < rounds.Count; i++)
                    {
                        var r = rounds[i];
                        writer.Write(source.Substring(r.Key, r.Value));
                        writer.Flush();
                    }

                    for (var i = 0; i < size; i++)
                    {
                        var receivedLine = reader.ReadLine();
                        Assert.AreEqual(lines[i], receivedLine);
                    }
                }
            }
        }


        [Test]
        public void TestUdpRequest()
        {
            EndPoint serverAddress = new IPEndPoint(IPAddress.Parse("127.0.0.1"), 2012);
            var client = new Socket(serverAddress.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

            var line = Guid.NewGuid().ToString();

            var data = Encoding.ASCII.GetBytes(CreateRequest(line));

            client.SendTo(data, serverAddress);

            var receiveData = new byte[data.Length];

            var len = client.ReceiveFrom(receiveData, ref serverAddress);

            Assert.AreEqual(line.Length, len);
            Assert.AreEqual(line, Encoding.ASCII.GetString(receiveData, 0, len));
        }
    }
}
