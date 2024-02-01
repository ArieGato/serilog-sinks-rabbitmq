// Copyright 2015-2022 Serilog Contributors
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

global using System;
global using System.Text;
global using System.Threading.Tasks;
global using FluentAssertions;
global using Newtonsoft.Json.Linq;
global using RabbitMQ.Client;
global using RabbitMQ.Client.Events;
global using RabbitMQ.Client.Exceptions;
global using Serilog.Core;
global using Serilog.Formatting.Json;
global using Xunit;
