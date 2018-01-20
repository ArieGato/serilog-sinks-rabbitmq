// Copyright 2018 Serilog Contributors
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Text;

namespace Serilog.Sinks.RabbitMQ.Sinks.RabbitMQ
{
    public static class MessageEncoder
    {
        public static byte[] Encode(string text, EncodingMode mode)
        {
            Encoding encoding;
            switch (mode)
            {
                case EncodingMode.UTF8: encoding = Encoding.UTF8;break;                   
                case EncodingMode.Unicode: encoding = Encoding.Unicode;break;                   
                case EncodingMode.ASCII: encoding = Encoding.ASCII;break;                   
                case EncodingMode.UTF32: encoding = Encoding.UTF32;break;                   
                case EncodingMode.UTF7: encoding = Encoding.UTF7;break;                   
                
                // by default compatible with previous versions
                default: encoding = Encoding.UTF8; break;                
            }

            return encoding.GetBytes(text);
        }
    }
}