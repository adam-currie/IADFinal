/**
* @file		IChatNode.cs
* @project	AutoServerChat
* @author	Adam Currie & Alexander Martin
* @date		2016-04-7
 */
using System;

namespace AutoServerChat {

    /**
     * @class   MessageSaidEventArgs
     *
     * @brief   Contains the message and sender's name for a MessageSaid event.
     */
    public class MessageSaidEventArgs : EventArgs{
        public readonly string Name, Message;
        public MessageSaidEventArgs(string name, string message) {
            Name = name;
            Message = message;
        }
    }

    /**
     * @interface   IChatNode
     *
     * @brief   Interface for chat nodes.
     */
    internal interface IChatNode {

        /** 
         * @brief   Event queue for all listeners interested in MessageSaid events. 
         *          
         * @details Raised when a message has been said by anyone involved in the chat session.
         *          This includes messages said by this node.
         *          Server messages have the name "SERVER".
         */
        event EventHandler<MessageSaidEventArgs> MessageSaid;

        /**
         * @property    string Name
         *
         * @brief   Gets or sets the name that identifies messages from this node.
         *
         * @return  The name or null if unset.
         */
        string Name {
            get;
            set;
        }

        /**
         * @fn  void Say(string msg);
         *
         * @brief   Says the message specified by msg, visable to all members of the chat session.
         *
         * @param   msg The message.
         */
        void Say(string msg);
    }
}
