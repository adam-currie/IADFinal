using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoServerChat {
    internal static class Protocol {
        public const ushort PORT = 43993;

        //message identifiers
        public const byte SERVER_INFO_REQUEST = 1;
        public const byte SERVER_INFO = 2;
        public const byte SAY = 3;
        public const byte SET_NAME = 4;
        public const byte SAY_DISPATCH = 5;
    }
}
