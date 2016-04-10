using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using AutoServerChat;

namespace ChatApp {
    public partial class ChatForm : Form {
        ChatNode chat = new ChatNode();

        public ChatForm() {
            InitializeComponent();
            chat.MessageSaid += MessageSaidHandler;
            chat.Start();
        }

        private void MessageSaidHandler(object sender, MessageSaidEventArgs e) {
            if(outputTextBox.InvokeRequired) {
                outputTextBox.BeginInvoke(//re-tigger event on ui thread
                    new EventHandler<MessageSaidEventArgs>(MessageSaidHandler),
                    new object[] { sender, e }
                );
            } else {
                //add to output
                outputTextBox.AppendText(e.Name + ": " + e.Message + Environment.NewLine);
            }
        }

        private void sendButton_Click(object sender, EventArgs e) {
            try {
                chat.Say(inputTextBox.Text);
                inputTextBox.Clear();
            } catch(Exception ex) {
                throw;//todo: catch exception saying that msg is empty or too long
            }
        }
    }
}
