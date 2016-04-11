/**
* @file		ChatForm.cs
* @project	ChatApp
* @author	Adam Currie & Alexander Martin
* @date		2016-04-7
 */
using System;
using System.Windows.Forms;
using AutoServerChat;

namespace ChatApp {

    /**
     * @class   ChatForm
     *
     * @brief   Form for viewing the chat.
     */
    public partial class ChatForm : Form {
        ChatNode chat = new ChatNode();

        /**
         * @fn  public ChatForm()
         *
         * @brief   Default constructor.
         */
        public ChatForm() {
            InitializeComponent();
            chat.MessageSaid += MessageSaidHandler;
            chat.Start();
        }

        /**
         * @fn  private void MessageSaidHandler(object sender, MessageSaidEventArgs e)
         *
         * @brief   Handler, Called when a message was said in the chat.
         *
         * @param   sender  Source of the event.
         * @param   e       Message said event information.
         */
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

        /**
         * @fn  private void sendButton_Click(object sender, EventArgs e)
         *
         * @brief   Event handler. Called by sendButton for click events, says something in the chat.
         *
         * @param   sender  Source of the event.
         * @param   e       Event information.
         */
        private void sendButton_Click(object sender, EventArgs e) {
            try {
                chat.Say(inputTextBox.Text);
                inputTextBox.Clear();
            } catch(ArgumentException) {
                //msg empty
            }
        }

        /**
         * @fn  private void SetNameEvent(object sender, EventArgs e)
         *
         * @brief   Event to set name of user in chat.
         *
         * @param   sender  Source of the event.
         * @param   e       Event information.
         */
        private void SetNameEvent(object sender, EventArgs e) {
            string name = nameTextBox.Text.Trim();
            if(name == "") {
                return;//cant be empty
            }
            try {
                chat.Name = name;
            } catch(ArgumentException ex) {
                MessageBox.Show(ex.Message, "Invalid Name", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
            }
        }
    }
}
