﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nebula.Core
{
    [ServiceBehavior(ConcurrencyMode = ConcurrencyMode.Multiple, InstanceContextMode = InstanceContextMode.Single)]
    public class Node : INode, INodeCallback, IDisposable
    {
        public IPEndPoint LocalEndPoint { get; private set; }

        private ConcurrentDictionary<INode,         IPEndPoint> m_hNodes;
        private ConcurrentDictionary<INodeCallback, IPEndPoint> m_hNodesCallback;

        private ServiceHost     m_hHost;
        private IMasterServer   m_hMs;

        public event Action<IPEndPoint> NodeFaulted;
        public event Action<IPEndPoint> NodeConnected;

        public Node()
        {
            m_hNodes            = new ConcurrentDictionary<INode, IPEndPoint>();
            m_hNodesCallback    = new ConcurrentDictionary<INodeCallback, IPEndPoint>();
        }

        public void Start(int iPort)
        {
            LocalEndPoint = new IPEndPoint(IPAddress.Loopback, iPort);

            m_hNodes.Clear();
            m_hHost = new ServiceHost(this, new Uri($"net.tcp://localhost:{iPort}/NebulaNode"));

            NetTcpBinding hBinding = new NetTcpBinding();
            hBinding.Security.Mode = SecurityMode.None;
            //hBinding.MaxBufferSize      = 51200;
            //hBinding.MaxBufferPoolSize  = 0;
            //hBinding.MaxReceivedMessageSize = 2147483647;

            m_hHost.AddServiceEndpoint(typeof(INode), hBinding, "");
            m_hHost.Open();


            ChannelFactory<IMasterServer> hMsFactory = new ChannelFactory<IMasterServer>(hBinding);
            m_hMs = hMsFactory.CreateChannel(new EndpointAddress($"net.tcp://37.187.154.24:40000/NebulaMasterServer"));
            m_hMs.Register();
        }

        public void Stop()
        {
            try
            {
                m_hHost.Close();
            }
            catch (Exception)
            {
            }
            finally
            {
                m_hHost = null;
                m_hNodes.Clear();
            }
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public IPEndPoint[] EnumerateNodes()
        {
            return m_hNodes.Values.Union(m_hNodesCallback.Values).ToArray();
        }


        /// <summary>
        /// Connect the node to another node
        /// </summary>
        /// <param name="sKnowAddr">Ip address to the reachable Node</param>
        /// <param name="iPort">the port where the node instance is listening</param>
        public IPEndPoint[] Connect(string sKnowAddr, int iPort)
        {
            NetTcpBinding hBinding                  = new NetTcpBinding();
            hBinding.Security.Mode                  = SecurityMode.None;
            DuplexChannelFactory<INode> hFactory    = new DuplexChannelFactory<INode>(typeof(Node), hBinding);
            InstanceContext hContext                = new InstanceContext(this);

            INode hFirstNode                        = hFactory.CreateChannel(hContext, new EndpointAddress($"net.tcp://{sKnowAddr}:{iPort}/NebulaNode"));

            if (!m_hNodes.TryAdd(hFirstNode, new IPEndPoint(IPAddress.Parse(sKnowAddr), iPort)))
                throw new Exception("Could Not Connect to the Node");

            //Get back some addresses
            IPEndPoint[] hResult = hFirstNode.Join(5) ?? new IPEndPoint[0];

            //TODO: deferred network connection, for now just connect to the other existing nodes
            foreach (IPEndPoint hRemoteEndPoint in hResult)
            {
                INode hNewNode = hFactory.CreateChannel(hContext, new EndpointAddress($"net.tcp://{hRemoteEndPoint.Address.ToString()}:{hRemoteEndPoint.Port}/NebulaNode"));
                hNewNode.Join(0);

                m_hNodes.TryAdd(hNewNode, hRemoteEndPoint);                    
            }

            return hResult;
        }

        

        //TODO: security (iMaxPeers very big)
        public IPEndPoint[] Join(int iMaxPeers)
        {
            INodeCallback   hCb             = OperationContext.Current.GetCallbackChannel<INodeCallback>();
            IPEndPoint      hRemoteEndPoint = OperationContext.Current.GetRemoteEndPoint();            
            IPEndPoint[]    hResult         = m_hNodes.Values.ToArray();                                        //get only accessible addresses

            (hCb as ICommunicationObject).Faulted += OnFaulted;

            if (m_hNodesCallback.TryAdd(hCb, hRemoteEndPoint))
            {
                NodeConnected?.Invoke(hRemoteEndPoint);
                return hResult;
            }
            else
                return null;
        }


        public void PlaceHolder()
        {
        }

        private void OnFaulted(object sender, EventArgs e)
        {
            INode hClient = sender as INode;
            IPEndPoint hRemoved;

            if (m_hNodes.TryRemove(hClient, out hRemoved))
            {
                NodeFaulted?.Invoke(hRemoved);
            }
            else
            {
                //TODO: accrocco temporaneo, prevedere sistema per la notifica dei  dei client mancanti
                throw new NotImplementedException();
            }
        }

        #region ContractOperations
        #endregion





        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.Stop();
                }
                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }



        #endregion
    }

}
