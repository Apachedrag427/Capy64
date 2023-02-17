﻿// This file is part of Capy64 - https://github.com/Ale32bit/Capy64
// Copyright 2023 Alessandro "AlexDevs" Proto
//
// Licensed under the Apache License, Version 2.0 (the "License").
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

using Capy64.API;
using KeraLua;
using System;

namespace Capy64.Runtime.Libraries;

public class Event : IComponent
{
    private static IGame _game;
    public Event(IGame game)
    {
        _game = game;
    }

    private static LuaRegister[] EventLib = new LuaRegister[]
    {
        new()
        {
            name = "pull",
            function = L_Pull,
        },
        new()
        {
            name = "pullRaw",
            function = L_PullRaw
        },
        new()
        {
            name = "push",
            function = L_Push,
        },
        new(),
    };

    public void LuaInit(Lua L)
    {
        L.RequireF("event", OpenLib, false);
    }

    private static int OpenLib(IntPtr state)
    {
        var L = Lua.FromIntPtr(state);
        L.NewLib(EventLib);
        return 1;
    }

    private static int LK_Pull(IntPtr state, int status, IntPtr ctx)
    {
        var L = Lua.FromIntPtr(state);

        if (L.ToString(1) == "interrupt")
        {
            L.Error("interrupt");
        }

        var nargs = L.GetTop();

        return nargs;
    }

    private static int L_Pull(IntPtr state)
    {
        var L = Lua.FromIntPtr(state);

        var nargs = L.GetTop();
        for (int i = 1; i <= nargs; i++)
        {
            L.CheckString(i);
        }

        L.YieldK(nargs, 0, LK_Pull);

        return 0;
    }

    private static int L_PullRaw(IntPtr state)
    {
        var L = Lua.FromIntPtr(state);

        var nargs = L.GetTop();
        for (int i = 1; i <= nargs; i++)
        {
            L.CheckString(i);
        }

        L.Yield(nargs);

        return 0;
    }

    private static int L_Push(IntPtr state)
    {
        var L = Lua.FromIntPtr(state);

        var eventName = L.CheckString(1);

        var nargs = L.GetTop();
        var parsState = L.NewThread();
        L.Pop(1);
        L.XMove(parsState, nargs - 1);

        _game.LuaRuntime.QueueEvent(eventName, LK =>
        {
            var nargs = parsState.GetTop();
            parsState.XMove(LK, nargs);

            return nargs;
        });

        return 0;
    }
}
