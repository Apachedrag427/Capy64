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
using Capy64.Core;
using Capy64.Eventing;
using Capy64.Extensions;
using Capy64.Integrations;
using Capy64.PluginManager;
using Capy64.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using static Capy64.Utils;

namespace Capy64;

public enum EngineMode
{
    Classic,
    Free
}

public class Capy64 : Game
{
    public const string Version = "1.1.0-beta";

    public static class DefaultParameters
    {
        public const int Width = 318;
        public const int Height = 240;
        public const float Scale = 2f;
        public const float BorderMultiplier = 1.5f;
        public static readonly EngineMode EngineMode = EngineMode.Classic;

        public const int ClassicTickrate = 30;
        public const int FreeTickrate = 60;
    }

    public static readonly string AssemblyPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    public static readonly string AssetsPath = Path.Combine(AssemblyPath, "Assets");

    public static string AppDataPath
    {
        get
        {
            string baseDir =
                RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ?
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData,
                        Environment.SpecialFolderOption.Create) :
                RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ?
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                        Environment.SpecialFolderOption.Create) :
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ?
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData,
                        Environment.SpecialFolderOption.Create) :
                "./";

            return Path.Combine(baseDir, "Capy64");
        }
    }

    public static Capy64 Instance { get; private set; }
    public Capy64 Game => this;
    public EngineMode EngineMode { get; private set; } = EngineMode.Classic;
    public IList<IComponent> NativePlugins { get; private set; }
    public IList<IComponent> Plugins { get; private set; }
    public int Width { get; set; } = DefaultParameters.Width;
    public int Height { get; set; } = DefaultParameters.Height;
    public float Scale { get; set; } = DefaultParameters.Scale;
    public Drawing Drawing { get; private set; }
    public Audio Audio { get; private set; }
    public LuaState LuaRuntime { get; set; }
    public Eventing.EventEmitter EventEmitter { get; private set; }
    public DiscordIntegration Discord { get; set; }
    public int TickRate => tickrate;
    public IConfiguration Configuration { get; private set; }

    public Color BorderColor { get; set; } = Color.Black;

    public Borders Borders = new()
    {
        Top = 0,
        Bottom = 0,
        Left = 0,
        Right = 0,
    };

    public SpriteBatch SpriteBatch;


    private readonly InputManager _inputManager;
    private RenderTarget2D renderTarget;
    private readonly GraphicsDeviceManager _graphics;
    private ulong _totalTicks = 0;
    private int tickrate = 0;
    private int everyTick => 60 / tickrate;

    public Capy64()
    {
        Instance = this;

        _graphics = new GraphicsDeviceManager(this);
        //Content.RootDirectory = "Content";
        IsMouseVisible = true;

        EventEmitter = new();
        _inputManager = new(this, EventEmitter);

        Drawing = new();
    }

    public void SetEngineMode(EngineMode mode)
    {
        switch (mode)
        {
            case EngineMode.Classic:
                tickrate = DefaultParameters.ClassicTickrate;
                Width = DefaultParameters.Width;
                Height = DefaultParameters.Height;
                Window.AllowUserResizing = false;
                ResetBorder();

                break;

            case EngineMode.Free:
                tickrate = DefaultParameters.FreeTickrate;
                Window.AllowUserResizing = true;
                break;
        }

        EngineMode = mode;

        UpdateSize(true);
    }

    public void UpdateSize(bool resize = true)
    {
        if (resize)
        {
            _graphics.PreferredBackBufferWidth = (int)(Width * Scale) + Borders.Left + Borders.Right;
            _graphics.PreferredBackBufferHeight = (int)(Height * Scale) + Borders.Top + Borders.Bottom;
            _graphics.ApplyChanges();
        }

        renderTarget = new RenderTarget2D(
            GraphicsDevice,
            Width,
            Height,
            false,
            GraphicsDevice.PresentationParameters.BackBufferFormat,
            DepthFormat.Depth24, 0, RenderTargetUsage.PreserveContents);

        Drawing.Canvas = renderTarget;

        _inputManager.Texture = renderTarget;

        EventEmitter.RaiseScreenSizeChange();
    }

    private void OnWindowSizeChange(object sender, EventArgs e)
    {
        if (EngineMode == EngineMode.Classic)
        {
            UpdateSize(true);
            return;
        }

        var bounds = Window.ClientBounds;

        Width = (int)(bounds.Width / Scale);
        Height = (int)(bounds.Height / Scale);

        if (Window.IsMaximized())
        {
            var vertical = bounds.Height - (Height * Scale);
            var horizontal = bounds.Width - (Width * Scale);

            Borders.Top = (int)Math.Floor(vertical / 2d);
            Borders.Bottom = (int)Math.Ceiling(vertical / 2d);

            Borders.Left = (int)Math.Floor(horizontal / 2d);
            Borders.Right = (int)Math.Ceiling(horizontal / 2d);

            UpdateSize(false);
        }
        else
        {
            ResetBorder();
            UpdateSize();
        }
    }

    private void ResetBorder()
    {
        var size = (int)(Scale * DefaultParameters.BorderMultiplier);
        Borders = new Borders
        {
            Top = size,
            Bottom = size,
            Left = size,
            Right = size
        };
    }

    protected override void Initialize()
    {
        var configBuilder = new ConfigurationBuilder();

        var settingsPath = Path.Combine(AppDataPath, "settings.json");
        if (!Directory.Exists(AppDataPath))
        {
            Directory.CreateDirectory(AppDataPath);
        }
        if (!File.Exists(settingsPath))
        {
            File.Copy(Path.Combine(AssetsPath, "default.json"), settingsPath);
        }

        configBuilder.AddJsonFile(Path.Combine(AssetsPath, "default.json"), false);
        configBuilder.AddJsonFile(settingsPath, false);

        Configuration = configBuilder.Build();

        Window.Title = "Capy64 " + Version;

        Scale = Configuration.GetValue("Window:Scale", DefaultParameters.Scale);

        ResetBorder();
        UpdateSize();

        Window.AllowUserResizing = true;
        Window.ClientSizeChanged += OnWindowSizeChange;

        InactiveSleepTime = new TimeSpan(0);

        SetEngineMode(Configuration.GetValue<EngineMode>("EngineMode", DefaultParameters.EngineMode));

        Audio = new Audio();

        NativePlugins = GetNativePlugins();
        var safeMode = Configuration.GetValue("SafeMode", false);
        if (!safeMode)
            Plugins = PluginLoader.LoadAllPlugins(Path.Combine(AppDataPath, "plugins"));

        EventEmitter.RaiseInit();

        base.Initialize();
    }

    private List<IComponent> GetNativePlugins()
    {
        var iType = typeof(IComponent);
        var types = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(s => s.GetTypes())
            .Where(p => iType.IsAssignableFrom(p) && !p.IsInterface);

        var plugins = new List<IComponent>();

        foreach (var type in types)
        {
            var instance = (IComponent)Activator.CreateInstance(type, this);
            plugins.Add(instance);
        }

        return plugins;
    }


    protected override void LoadContent()
    {
        SpriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        Drawing.Begin();

        // Register user input
        _inputManager.Update(IsActive);

        EventEmitter.RaiseTick(new()
        {
            GameTime = gameTime,
            TotalTicks = _totalTicks,
            IsActiveTick = (int)_totalTicks % everyTick == 0,
        });

        Drawing.End();

        _totalTicks++;

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        SpriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp);
        GraphicsDevice.Clear(BorderColor);

        SpriteBatch.DrawRectangle(renderTarget.Bounds.Location.ToVector2() + new Vector2(Borders.Left, Borders.Top),
            new Size2(renderTarget.Bounds.Width * Scale, renderTarget.Bounds.Height * Scale), Color.Black,
            Math.Min(renderTarget.Bounds.Width, renderTarget.Bounds.Height), 0);

        SpriteBatch.Draw(renderTarget, new(Borders.Left, Borders.Top), null, Color.White, 0f, Vector2.Zero, Scale,
            SpriteEffects.None, 0);

        EventEmitter.RaiseOverlay(new()
        {
            GameTime = gameTime,
            TotalTicks = _totalTicks,
        });

        SpriteBatch.End();

        base.Draw(gameTime);
    }
}