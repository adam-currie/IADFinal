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
        }

        private void MessageSaidHandler(object sender, MessageSaidEventArgs e) {
            throw new NotImplementedException();
        }
    }
}
