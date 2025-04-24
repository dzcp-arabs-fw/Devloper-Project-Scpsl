using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using DZCP.API.Models;
using DZCP.Logging;
using DZCP.NewEdition;
using HarmonyLib;
using PluginAPI.Core;
using PluginAPI.Core.Attributes;
using PluginAPI.Events;
using PluginAPI.Loader.Features;
using PluginFramework;
using YamlDotNet.Serialization;



namespace DZCP.NewEdition
{
    public class DZCPCore
    {
        public static DZCPCore Instance { get; private set; }

        public readonly string BaseDir = "/home/container/DZCP";
        public string ConfigsDir => Path.Combine(BaseDir, "Configs");
        public string LogsDir => Path.Combine(BaseDir, "Logs");
        public string ManagedDir => Path.Combine(BaseDir, "Managed");
        public string PatchesDir { get; set; }
        public string PluginsDir => Path.Combine(BaseDir, "Plugins");


        public string DependenciesDir => Path.Combine(BaseDir, "Dependencies");
        public string Managed  => Path.Combine(BaseDir, "Managed");    


        public string TranslationsPath => Path.Combine(BaseDir, "Translations", "translations.yml");
        public string IndividualTranslationsPath => Path.Combine(BaseDir, "Translations", "Individual");
        public string BackupTranslationsPath => Path.Combine(BaseDir, "Translations", "backup_translations.yml");

        public List<IDZCPPlugin> LoadedPlugins { get; } = new List<IDZCPPlugin>();
        public ConfigTypeDzcp ConfigType { get; set; } = ConfigTypeDzcp.Default;

        public ISerializer Serializer { get; private set; }
        public IDeserializer Deserializer { get; private set; }
        public List<IDZCPPatch> LoadedPatches { get; } = new List<IDZCPPatch>();
        public HashSet<Assembly> LoadedDependencies { get; } = new HashSet<Assembly>();

        [PluginEntryPoint("DZCP New Edition", "2.5.0", "Advanced Framework for SCP:SL", "DZCP Team:MONCEF50G")]
        public void Initialize()
        {
            Instance = this;
            Serializer = new SerializerBuilder()
                .DisableAliases()
                .Build();

            Deserializer = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .Build();
            CreateDirectories();
            ServerConsole.AddLog("[DZCP] Core initialized successfully!", ConsoleColor.Green);

            // تهيئة المسارات
            PatchesDir = Path.Combine(BaseDir, "Patches");

            try
            {
                // عرض شعار النظام
                DZCPBanner.Display();

                // إنشاء الهيكل الأساسي
                CreateDirectories();

                // تحميل التكوينات
                LoadConfigurations();

                // تهيئة Harmony
                var harmony = new Harmony("dzcp.newedition");
                harmony.PatchAll();

                // تحميل التبعيات
                LoadDependencies();

                // تحميل الباتشات
                LoadPatches();
                Assembly.Load(new AssemblyName("DZCP"));
                Assembly.Load(new AssemblyName("Plugins"));
                AssemblyLoadEventHandler.Combine();
                Loader.Loader.LoadAll(PluginsDir);
                Loader.PluginLoader.LoadAll();
                PluginLoaderDzcp.LoadPlugin(PluginsDir);
                
                // تحميل الإضافات
                LoadPlugins();

                // تسجيل الأوامر
                RegisterCommands();

                // تكامل مع SCP:SL عبر Server.Started
                OnServerStarted();

                ServerConsole.AddLog("[DZCP] Framework loaded successfully!", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Configuration error: {ex}", ConsoleColor.Red);
            }
        }
       
        /// <summary>
        /// يحاول إنشاء المجلد إذا كان المسار صالحًا.
        /// </summary>
        private void TryCreateDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ServerConsole.AddLog($"[DZCP] Directory path is null or empty: {path}", ConsoleColor.Red);
                return;
            }

            try
            {
                Directory.CreateDirectory(path);
                ServerConsole.AddLog($"[DZCP] Directory created: {path}", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Failed to create directory {path}: {ex.Message}", ConsoleColor.Red);
            }
        }
        public void CreateDirectories()
        {
            ServerConsole.AddLog($"[DZCP] BaseDir: {BaseDir}");
            ServerConsole.AddLog($"[DZCP] PluginsDir: {PluginsDir}");
            ServerConsole.AddLog($"[DZCP] ConfigsDir: {ConfigsDir}");
            ServerConsole.AddLog($"[DZCP] LogsDir: {LogsDir}");
            ServerConsole.AddLog($"[DZCP] DependenciesDir: {Managed}");
            ServerConsole.AddLog($"[DZCP] DependenciesDir: {ManagedDir}");
            ServerConsole.AddLog($"[DZCP] PatchesDir: {PatchesDir}");
            ServerConsole.AddLog($"[DZCP] DependenciesDir: {DependenciesDir}");

            TryCreateDirectory(BaseDir);
            TryCreateDirectory(PluginsDir);
            TryCreateDirectory(ConfigsDir);
            TryCreateDirectory(ManagedDir);
            TryCreateDirectory(Managed);
            TryCreateDirectory(LogsDir);
            TryCreateDirectory(PatchesDir);
            TryCreateDirectory(DependenciesDir);
        }
        
        public enum ConfigTypeDzcp
        {
            Default,
            Separated
        }
    

        private void LoadConfigurations()
        {
            string configPath = Path.Combine(ConfigsDir, "DZCPConfig.yml");

            try
            {
                if (File.Exists(configPath))
                {
                    // قراءة التكوين
                    string configContent = File.ReadAllText(configPath);
                    ServerConsole.AddLog($"[DZCP] Configuration loaded from {configPath}", ConsoleColor.Green);
                }
                else
                {
                    // إنشاء ملف تكوين افتراضي إذا لم يكن موجودًا
                    File.WriteAllText(configPath, "default_configuration: true");
                    ServerConsole.AddLog($"[DZCP] Default configuration file created at {configPath}", ConsoleColor.Yellow);
                }
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error loading configuration: {ex.Message}", ConsoleColor.Red);
            }
        }

        private void LoadDependencies()
        {
            if (!Directory.Exists(DependenciesDir)) return;

            foreach (var dll in Directory.GetFiles(DependenciesDir, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.Load(File.ReadAllBytes(dll));
                    LoadedDependencies.Add(assembly);
                    ServerConsole.AddLog($"[DZCP] Dependency loaded: {assembly.FullName}", ConsoleColor.Blue);
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Error loading dependency: {ex.Message}", ConsoleColor.DarkRed);
                }
            }
        }

        private void LoadPatches()
        {
            if (!Directory.Exists(PatchesDir)) return;

            foreach (var dll in Directory.GetFiles(PatchesDir, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var patchTypes = assembly.GetTypes()
                        .Where(t => typeof(IDZCPPatch).IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var type in patchTypes)
                    {
                        var patch = (IDZCPPatch)Activator.CreateInstance(type);
                        patch.Apply();
                        LoadedPatches.Add(patch);
                        ServerConsole.AddLog($"[DZCP] Patch loaded: {patch.Name}", ConsoleColor.Blue);
                    }
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Error loading patch: {ex.Message}", ConsoleColor.DarkRed);
                }
            }
        }

        private void LoadPlugins()
        {
            if (!Directory.Exists(PluginsDir))
            {
                ServerConsole.AddLog("[DZCP] Plugins folder not found!", ConsoleColor.Yellow);
                return;
            }

            foreach (var dll in Directory.GetFiles(PluginsDir, "*.dll"))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(dll);
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IDZCPPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var type in pluginTypes)
                    {
                        var plugin = (IDZCPPlugin)Activator.CreateInstance(type);
                        plugin.OnEnabled();
                        LoadedPlugins.Add(plugin);
                        Logger.Info($"✅ Loaded plugin: {plugin.Name} v{plugin.Version}");
                        ServerConsole.AddLog($"[DZCP] Loaded plugin: {plugin.Name} v{plugin.Version}", ConsoleColor.Cyan);
                    }
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Error loading {Path.GetFileName(dll)}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }

        private void RegisterCommands()
        {
            ServerConsole.AddLog("[DZCP] Registering commands...", ConsoleColor.Green);
            // تسجيل الأوامر الافتراضية أو أوامر الإضافات
        }

        private void OnServerStarted()
        {
            ServerConsole.AddLog("[DZCP] Server started, integrating plugins...", ConsoleColor.Green);

            foreach (var plugin in LoadedPlugins)
            {
                try
                {
                    plugin.OnEnabled();
                    Logger.Info($"✅ Loaded plugin: {plugin.Name} v{plugin.Version}");
                    ServerConsole.AddLog($"[DZCP] Plugin active: {plugin.Name}", ConsoleColor.Cyan);
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Error enabling plugin {plugin.Name}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }
    }

    public static class DZCPBanner
    {
        public static void Display()
        {
            var banner = new[]
            {
                " ",
                "██████╗ ███████╗ ██████╗██████╗     ███╗   ██╗███████╗██╗    ██╗",
                " ██╔══██╗╚══███╔╝██╔════╝██╔══██╗    ████╗  ██║██╔════╝██║    ██║",
                " ██║  ██║  ███╔╝ ██║     ██████╔╝    ██╔██╗ ██║█████╗  ██║ █╗ ██║",
                " ██║  ██║ ███╔╝  ██║     ██╔═══╝     ██║╚██╗██║██╔══╝  ██║███╗██║",
                " ██████╔╝███████╗╚██████╗██║         ██║ ╚████║███████╗╚███╔███╔╝",
                " ╚═════╝ ╚══════╝ ╚═════╝╚═╝         ╚═╝  ╚═══╝╚══════╝ ╚══╝╚══╝ ",
                " ",
                " DZCP Framework v2.5.0 - Advanced Plugin Loader",
                " =====================================",
                $" Loaded at: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                " "
            };

            foreach (var line in banner)
            {
                var color = line.Contains("█") ? ConsoleColor.DarkMagenta :
                            line.Contains("===") ? ConsoleColor.Gray : ConsoleColor.Yellow;
                ServerConsole.AddLog(line, color);
            }
        }
    }

    public interface IDZCPPlugin
    {
        string Name { get; }
        string Author { get; }
        Version Version { get; }
        void OnEnabled();
        void OnDisabled();
    }

    public interface IDZCPPatch
    {
        string Name { get; }
        void Apply();
        void Unapply();
    }
}
          public static class DZCPAssemblyLoader
    {
        /// <summary>
        /// Gets a list of all recognized and loaded plugins.
        /// </summary>
        public static Dictionary<Assembly, Dictionary<Type, IDZCPPlugin>> Plugins { get; } = new();

        /// <summary>
        /// Gets a list of all recognized and loaded dependencies.
        /// </summary>
        public static HashSet<Assembly> Dependencies { get; } = new();

        /// <summary>
        /// Whether the loader has been run already.
        /// </summary>
        public static bool IsLoaded { get; private set; } = false;

        /// <summary>
        /// Initializes the DZCP Assembly Loader.
        /// </summary>
        public static void Initialize()
        {
            if (IsLoaded)
                return;

            IsLoaded = true;
            ServerConsole.AddLog("[DZCP] Starting plugin and dependency loader...", ConsoleColor.Green);

            // Load dependencies
            LoadDependencies(DZCPCore.Instance.DependenciesDir);

            // Load plugins
            LoadPlugins(DZCPCore.Instance.PluginsDir);

            // Activate plugins
            ActivatePlugins();

            ServerConsole.AddLog("[DZCP] Plugin and dependency system is ready!", ConsoleColor.Green);
        }

        /// <summary>
        /// Loads plugins from a specified directory.
        /// </summary>
        /// <param name="directory">The directory containing plugin assemblies.</param>
        private static void LoadPlugins(string directory)
        {
            if (!Directory.Exists(directory))
            {
                ServerConsole.AddLog($"[DZCP] Plugins directory not found: {directory}", ConsoleColor.Yellow);
                return;
            }

            string[] pluginFiles = Directory.GetFiles(directory, "*.dll");
            ServerConsole.AddLog($"[DZCP] Found {pluginFiles.Length} plugin(s) to load.", ConsoleColor.Cyan);

            foreach (string file in pluginFiles)
            {
                if (!TryLoadAssembly(file, out Assembly assembly))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to load types from {file}: {ex.Message}", ConsoleColor.Red);
                    continue;
                }

                foreach (Type type in types)
                {
                    if (!typeof(IDZCPPlugin).IsAssignableFrom(type) || type.IsAbstract)
                        continue;

                    try
                    {
                        IDZCPPlugin pluginInstance = (IDZCPPlugin)Activator.CreateInstance(type);
                        if (!Plugins.ContainsKey(assembly))
                            Plugins[assembly] = new Dictionary<Type, IDZCPPlugin>();

                        Plugins[assembly][type] = pluginInstance;
                        ServerConsole.AddLog($"[DZCP] Loaded plugin: {pluginInstance.Name} v{pluginInstance.Version}", ConsoleColor.Cyan);
                    }
                    catch (Exception ex)
                    {
                        ServerConsole.AddLog($"[DZCP] Failed to initialize plugin {type.Name}: {ex.Message}", ConsoleColor.Red);
                    }
                }
            }
        }

        /// <summary>
        /// Loads dependencies from a specified directory.
        /// </summary>
        /// <param name="directory">The directory containing dependency assemblies.</param>
        private static void LoadDependencies(string directory)
        {
            if (!Directory.Exists(directory))
            {
                ServerConsole.AddLog($"[DZCP] Dependencies directory not found: {directory}", ConsoleColor.Yellow);
                return;
            }

            string[] dependencyFiles = Directory.GetFiles(directory, "*.dll");
            ServerConsole.AddLog($"[DZCP] Found {dependencyFiles.Length} dependency file(s).", ConsoleColor.Cyan);

            foreach (string file in dependencyFiles)
            {
                if (!TryLoadAssembly(file, out Assembly assembly))
                    continue;

                Dependencies.Add(assembly);
                ServerConsole.AddLog($"[DZCP] Loaded dependency: {assembly.GetName().Name} v{assembly.GetName().Version}", ConsoleColor.Blue);
            }
        }

        /// <summary>
        /// Attempts to load an assembly from a specified path.
        /// </summary>
        /// <param name="path">The path to the assembly file.</param>
        /// <param name="assembly">The loaded assembly, or null if loading failed.</param>
        /// <returns>True if the assembly was loaded successfully, otherwise false.</returns>
        private static bool TryLoadAssembly(string path, out Assembly assembly)
        {
            try
            {
                assembly = Assembly.Load(File.ReadAllBytes(path));
                return true;
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Failed to load assembly from {path}: {ex.Message}", ConsoleColor.Red);
                assembly = null;
                return false;
            }
        }
        public static PluginDirectory GlobalPlugins { get; private set; }


        /// <summary>
        /// Activates all loaded plugins.
        /// </summary>
        private static void ActivatePlugins()
        {
            foreach (var pluginEntry in Plugins.Values.SelectMany(p => p.Values))
            {
                try
                {
                    pluginEntry.OnEnabled();
                    Logger.Info($"✅ Loaded plugin: {pluginEntry.Name} v{pluginEntry.Version}");
                    ServerConsole.AddLog($"[DZCP] Activated plugin: {pluginEntry.Name}", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to activate plugin {pluginEntry.Name}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }
               /// <summary>
    /// DZCP Bootstrapper responsible for initializing the framework and loading core components.
    /// </summary>
    public class DZCPBootstrap
    {
        [PluginEntryPoint(
            "DZCPBootstrap",
            "1.0.0",
            "Initializes the DZCP framework and loads required components",
            "MONCEF50G"
        )]
        public void Execute()
        {
            try
            {
                // Check for startup arguments
                if (StartupArgs.Args.Any(arg => arg.Equals("-nodzcp", StringComparison.OrdinalIgnoreCase)))
                {
                    Log("Server started with -nodzcp argument! DZCP framework will not be loaded", ConsoleColor.Yellow);
                    return;
                }

                Log("Bootstrapping DZCP Framework", ConsoleColor.Cyan);

                var assemblies = new List<Assembly>();
                var domain = AppDomain.CurrentDomain;

                // Define the path for DZCP managed libraries
                var managedPath = Path.Combine(Directory.GetCurrentDirectory(), "DZCP", "Managed");

                // Ensure the directory exists
                if (!Directory.Exists(managedPath))
                {
                    Log($"Managed directory not found at {managedPath}", ConsoleColor.Red);
                    return;
                }

                // Load all DLLs from the Managed directory
                foreach (var file in Directory.GetFiles(managedPath, "*.dll"))
                {
                    try
                    {
                        var assembly = domain.Load(File.ReadAllBytes(file));
                        assemblies.Add(assembly);
                        Log($"Loaded assembly: {assembly.GetName().Name}", ConsoleColor.Green);
                    }
                    catch (Exception ex)
                    {
                        Log($"Failed to load assembly: {file}\n{ex}", ConsoleColor.DarkRed);
                    }
                }

                // Resolve dependencies dynamically
                domain.AssemblyResolve += (sender, eventArgs) =>
                {
                    return assemblies.FirstOrDefault(x => x.FullName == eventArgs.Name);
                };

                // Find and initialize the DZCP core
                var coreAssembly = assemblies.FirstOrDefault(x => x.GetName().Name == "DZCP.Core");
                if (coreAssembly == null)
                    throw new Exception("DZCP.Core assembly not found");

                var entryPointType = coreAssembly.GetType("DZCP.NewEdition.DZCPCore");
                if (entryPointType == null)
                    throw new Exception("DZCPCore type not found in DZCP.Core");

                var initializeMethod = entryPointType.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static);
                if (initializeMethod == null)
                    throw new Exception("DZCPCore.Initialize() method not found");

                Log("Initializing DZCPCore...", ConsoleColor.Cyan);
                initializeMethod.Invoke(null, null);
            }
            catch (Exception e)
            {
                void log(string message, ConsoleColor color = ConsoleColor.Gray)
                {
                    DZCPBootstrap.DZCPPluginLoader.Plugins.All(plugin =>Assembly.GetEntryAssembly().GetName().Name != plugin.Name );
                }

                Log($"Failed to bootstrap DZCP Framework: {e}", ConsoleColor.DarkRed);
                ServerConsole.AddLog("[Bootstrap] " );

            }
        }
        
        
        /// <summary>
        /// Logs a message to the server console.
        /// </summary>
        /// <param name="message">The message to log.</param>
        /// <param name="color">The color of the message.</param>
        private void Log(string message, ConsoleColor color = ConsoleColor.Gray)
        {
            ServerConsole.AddLog("[Bootstrap] " + message, color);
        }
    
          public static class DZCPAssemblyLoader
    {
        private static Assembly _mainAssembly;

        /// <summary>
        /// Gets the main assembly for the game.
        /// </summary>
        public static Assembly MainAssembly
        {
            get
            {
                if (_mainAssembly == null)
                    _mainAssembly = typeof(DZCPCore).Assembly;
                return _mainAssembly;
            }
        }

        /// <summary>
        /// List of loaded plugins.
        /// </summary>
        public static Dictionary<Assembly, Dictionary<Type, IDZCPPlugin>> Plugins { get; } 
            = new Dictionary<Assembly, Dictionary<Type, IDZCPPlugin>>();

        /// <summary>
        /// List of loaded dependencies.
        /// </summary>
        public static HashSet<Assembly> Dependencies { get; } = new HashSet<Assembly>();

        /// <summary>
        /// Whether the loader has been initialized.
        /// </summary>
        public static bool IsInitialized { get; private set; } = false;

        /// <summary>
        /// Initializes the DZCP framework, loading plugins and dependencies.
        /// </summary>
        public static void Initialize()
        {
            if (IsInitialized)
                return;

            ServerConsole.AddLog("[DZCP] Initializing plugin loader...", ConsoleColor.Cyan);

            IsInitialized = true;

            DZCPCore.Instance.CreateDirectories();

            ServerConsole.AddLog("[DZCP] Loading dependencies...", ConsoleColor.Cyan);
            LoadDependencies(DZCPCore.Instance.DependenciesDir);

            ServerConsole.AddLog("[DZCP] Loading plugins...", ConsoleColor.Cyan);
            LoadPlugins(DZCPCore.Instance.PluginsDir);

            ServerConsole.AddLog("[DZCP] All plugins and dependencies loaded successfully!", ConsoleColor.Green);
        }

        /// <summary>
        /// Loads plugins from the specified directory.
        /// </summary>
        /// <param name="directory">The directory containing plugins.</param>
        private static void LoadPlugins(string directory)
        {
            if (!Directory.Exists(directory))
            {
                ServerConsole.AddLog($"[DZCP] Plugin directory not found: {directory}", ConsoleColor.Yellow);
                return;
            }

            var pluginFiles = Directory.GetFiles(directory, "*.dll");
            ServerConsole.AddLog($"[DZCP] Found {pluginFiles.Length} plugins to load.", ConsoleColor.Cyan);

            foreach (string pluginPath in pluginFiles)
            {
                if (!TryLoadAssembly(pluginPath, out var assembly))
                    continue;

                try
                {
                    var pluginTypes = assembly.GetTypes()
                        .Where(t => typeof(IDZCPPlugin).IsAssignableFrom(t) && !t.IsAbstract);

                    foreach (var type in pluginTypes)
                    {
                        var plugin = (IDZCPPlugin)Activator.CreateInstance(type);
                        if (!Plugins.ContainsKey(assembly))
                            Plugins[assembly] = new Dictionary<Type, IDZCPPlugin>();

                        Plugins[assembly][type] = plugin;
                        plugin.OnEnabled();
                        ServerConsole.AddLog($"[DZCP] Loaded plugin: {plugin.Name} v{plugin.Version}", ConsoleColor.Green);
                    }
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to load plugin from {pluginPath}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }

        /// <summary>
        /// Loads dependencies from the specified directory.
        /// </summary>
        /// <param name="directory">The directory containing dependencies.</param>
        private static void LoadDependencies(string directory)
        {
            if (!Directory.Exists(directory))
            {
                ServerConsole.AddLog($"[DZCP] Dependencies directory not found: {directory}", ConsoleColor.Yellow);
                return;
            }

            var dependencyFiles = Directory.GetFiles(directory, "*.dll");
            foreach (var dependencyPath in dependencyFiles)
            {
                if (!TryLoadAssembly(dependencyPath, out var assembly))
                    continue;

                Dependencies.Add(assembly);
                ServerConsole.AddLog($"[DZCP] Loaded dependency: {assembly.GetName().Name}", ConsoleColor.Blue);
            }
        }

        /// <summary>
        /// Attempts to load an assembly from the specified path.
        /// </summary>
        /// <param name="path">The path to the assembly.</param>
        /// <param name="assembly">The loaded assembly.</param>
        /// <returns>True if the assembly was loaded successfully; otherwise, false.</returns>
        private static bool TryLoadAssembly(string path, out Assembly assembly)
        {
            try
            {
                assembly = Assembly.Load(File.ReadAllBytes(path));
                return true;
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Failed to load assembly from {path}: {ex.Message}", ConsoleColor.Red);
                assembly = null;
                return false;
            }
        }
    }
           /// <summary>
    /// Manages initialization of plugins and dependencies in DZCP.
    /// </summary>
    public class DZCPPluginLoader
    {
        public static DZCPPluginLoader Instance { get; private set; }

        /// <summary>
        /// List of loaded plugins.
        /// </summary>
        public static SortedSet<IDZCPPlugin> Plugins { get; } = new(new PluginPriorityComparer());

        /// <summary>
        /// List of loaded dependencies.
        /// </summary>
        public static List<Assembly> Dependencies { get; } = new();

        /// <summary>
        /// Dictionary containing the file paths of loaded assemblies.
        /// </summary>
        public static Dictionary<Assembly, string> Locations { get; } = new();

        /// <summary>
        /// Initializes the plugin loader.
        /// </summary>
        public void Initialize()
        {
            Instance = this;

            try
            {
                ServerConsole.AddLog($"[DZCP] Initializing Plugin Loader at {Environment.CurrentDirectory}", ConsoleColor.Cyan);

                LoadDependencies(DZCPCore.Instance.DependenciesDir);
                LoadPlugins(DZCPCore.Instance.PluginsDir);

                EnablePlugins();
                ServerConsole.AddLog($"[DZCP] Plugin system initialized successfully!", ConsoleColor.Green);
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Plugin Loader initialization failed: {ex}", ConsoleColor.Red);
            }
        }

        /// <summary>
        /// Loads all plugins from the plugins directory.
        /// </summary>
        private void LoadPlugins(string directory)
        {
            if (!Directory.Exists(directory))
            {
                ServerConsole.AddLog($"[DZCP] Plugins directory not found: {directory}", ConsoleColor.Yellow);
                return;
            }

            foreach (var pluginPath in Directory.GetFiles(directory, "*.dll"))
            {
                var assembly = LoadAssembly(pluginPath);
                if (assembly == null)
                    continue;

                Locations[assembly] = pluginPath;

                foreach (var type in assembly.GetTypes())
                {
                    if (!typeof(IDZCPPlugin).IsAssignableFrom(type) || type.IsAbstract)
                        continue;

                    try
                    {
                        var plugin = (IDZCPPlugin)Activator.CreateInstance(type);
                        Plugins.Add(plugin);

                        ServerConsole.AddLog($"[DZCP] Loaded plugin: {plugin.Name} v{plugin.Version}", ConsoleColor.Cyan);
                    }
                    catch (Exception ex)
                    {
                        ServerConsole.AddLog($"[DZCP] Failed to load plugin {type.FullName}: {ex.Message}", ConsoleColor.Red);
                    }
                }
            }
        }

        /// <summary>
        /// Loads all dependencies from the dependencies directory.
        /// </summary>
        private void LoadDependencies(string directory)
        {
            if (!Directory.Exists(directory))
            {
                ServerConsole.AddLog($"[DZCP] Dependencies directory not found: {directory}", ConsoleColor.Yellow);
                return;
            }

            foreach (var dependencyPath in Directory.GetFiles(directory, "*.dll"))
            {
                var assembly = LoadAssembly(dependencyPath);
                if (assembly == null)
                    continue;

                Dependencies.Add(assembly);
                ServerConsole.AddLog($"[DZCP] Loaded dependency: {assembly.GetName().Name} v{assembly.GetName().Version}", ConsoleColor.Blue);
            }
        }

        /// <summary>
        /// Attempts to load an assembly from the specified path.
        /// </summary>
        private Assembly LoadAssembly(string path)
        {
            try
            {
                var assembly = Assembly.Load(File.ReadAllBytes(path));
                ResolveEmbeddedResources(assembly);
                return assembly;
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Failed to load assembly at {path}: {ex.Message}", ConsoleColor.Red);
                return null;
            }
        }

        /// <summary>
        /// Resolves embedded resources in an assembly.
        /// </summary>
        private void ResolveEmbeddedResources(Assembly assembly)
        {
            foreach (var resourceName in assembly.GetManifestResourceNames())
            {
                if (!resourceName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var resourceStream = assembly.GetManifestResourceStream(resourceName);
                if (resourceStream == null)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to resolve resource {resourceName}: Stream was null", ConsoleColor.Red);
                    continue;
                }

                using var memoryStream = new MemoryStream();
                resourceStream.CopyTo(memoryStream);

                try
                {
                    var embeddedAssembly = Assembly.Load(memoryStream.ToArray());
                    Dependencies.Add(embeddedAssembly);
                    ServerConsole.AddLog($"[DZCP] Loaded embedded assembly: {embeddedAssembly.GetName().Name}", ConsoleColor.Blue);
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to load embedded assembly {resourceName}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }

        /// <summary>
        /// Enables all loaded plugins.
        /// </summary>
        private void EnablePlugins()
        {
            foreach (var plugin in Plugins.ToList())
            {
                try
                {
                    plugin.OnEnabled();
                    ServerConsole.AddLog($"[DZCP] Enabled plugin: {plugin.Name}", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to enable plugin {plugin.Name}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }

        /// <summary>
        /// Disables all loaded plugins.
        /// </summary>
        public void DisablePlugins()
        {
            foreach (var plugin in Plugins)
            {
                try
                {
                    plugin.OnDisabled();
                }
                catch (Exception ex)
                {
                    ServerConsole.AddLog($"[DZCP] Failed to disable plugin {plugin.Name}: {ex.Message}", ConsoleColor.Red);
                }
            }
        }
    }

    /// <summary>
    /// Comparer for plugin priorities.
    /// </summary>
    public class PluginPriorityComparer : IComparer<IDZCPPlugin>
    {
        public static PluginPriorityComparer Instance { get; } = new();

        public int Compare(IDZCPPlugin x, IDZCPPlugin y) => x.Priority.CompareTo(y.Priority);
    }

    /// <summary>
    /// Interface for DZCP plugins.
    /// </summary>
    public interface IDZCPPlugin
    {
        string Name { get; }
        Version Version { get; }
        string Prefix { get; }
        IDZCPTranslation LoadTranslation(Dictionary<string, object> rawTranslations = null);

        IDZCPTranslation InternalTranslation { get; }
        int Priority { get; }
        void OnEnabled();
        void OnDisabled();
    }
}
               /// <summary>
    /// Manages plugin translations in the DZCP framework.
    /// </summary>
    public static class DZCPTranslationManager
    {
        /// <summary>
        /// Loads all plugin translations.
        /// </summary>
        /// <param name="rawTranslations">The raw translations to be loaded.</param>
        /// <returns>A dictionary of loaded translations.</returns>
        public static SortedDictionary<string, IDZCPTranslation> Load(string rawTranslations)
        {
            try
            {
                ServerConsole.AddLog("[DZCP] Loading plugin translations...", ConsoleColor.Cyan);

                Dictionary<string, object> rawDeserializedTranslations = DZCPCore.Instance.Deserializer.Deserialize<Dictionary<string, object>>(rawTranslations) ?? new Dictionary<string, object>();
                SortedDictionary<string, IDZCPTranslation> deserializedTranslations = new(StringComparer.Ordinal);

                foreach (var plugin in DZCPBootstrap.DZCPPluginLoader.Plugins)
                {
                    if (plugin.InternalTranslation is null)
                        continue;

                    deserializedTranslations.Add(plugin.Prefix, plugin.LoadTranslation(rawDeserializedTranslations));
                }

                if (!rawDeserializedTranslations.Keys.All(deserializedTranslations.ContainsKey))
                {
                    ServerConsole.AddLog($"[DZCP] Missing plugins detected in translations. Backup created at {DZCPCore.Instance.BackupTranslationsPath}", ConsoleColor.Yellow);
                    File.WriteAllText(DZCPCore.Instance.BackupTranslationsPath, rawTranslations);
                }

                ServerConsole.AddLog("[DZCP] Plugin translations loaded successfully.", ConsoleColor.Green);
                return deserializedTranslations;
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error while loading translations: {ex.Message}", ConsoleColor.Red);
                return null;
            }
        }

        /// <summary>
        /// Reloads all translations.
        /// </summary>
        /// <returns>True if reload was successful, false otherwise.</returns>
        public static bool Reload() => Save(Load(Read()));

        /// <summary>
        /// Saves translations for all plugins.
        /// </summary>
        /// <param name="translations">The translations to save.</param>
        /// <returns>True if save was successful, false otherwise.</returns>
        public static bool Save(SortedDictionary<string, IDZCPTranslation> translations)
        {
            try
            {
                if (translations == null || translations.Count == 0)
                    return false;

                if (DZCPCore.Instance.ConfigType == DZCPCore.ConfigTypeDzcp.Default)
                {
                    return SaveDefaultTranslation(DZCPCore.Instance.Serializer.Serialize(translations));
                }

                return translations.All(plugin => SaveSeparatedTranslation(plugin.Key, DZCPCore.Instance.Serializer.Serialize(plugin.Value)));
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error while saving translations: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Reads translations from file.
        /// </summary>
        /// <returns>The raw translations as a string.</returns>
        public static string Read()
        {
            if (DZCPCore.Instance.ConfigType != DZCPCore.ConfigTypeDzcp.Default)
                return string.Empty;

            try
            {
                if (File.Exists(DZCPCore.Instance.TranslationsPath))
                    return File.ReadAllText(DZCPCore.Instance.TranslationsPath);
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error reading translations: {ex.Message}", ConsoleColor.Red);
            }

            return string.Empty;
        }

        /// <summary>
        /// Saves default translations to a single file.
        /// </summary>
        /// <param name="translations">The translations to save.</param>
        /// <returns>True if save was successful, false otherwise.</returns>
        public static bool SaveDefaultTranslation(string translations)
        {
            try
            {
                File.WriteAllText(DZCPCore.Instance.TranslationsPath, translations ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error saving default translations: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Saves translations for a specific plugin.
        /// </summary>
        /// <param name="pluginPrefix">The plugin prefix.</param>
        /// <param name="translations">The translations to save.</param>
        /// <returns>True if save was successful, false otherwise.</returns>
        public static bool SaveSeparatedTranslation(string pluginPrefix, string translations)
        {
            string path = Path.Combine(DZCPCore.Instance.IndividualTranslationsPath, pluginPrefix);

            try
            {
                Directory.CreateDirectory(path);
                File.WriteAllText(Path.Combine(path, "translations.yml"), translations ?? string.Empty);
                return true;
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error saving translations for {pluginPrefix}: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }

        /// <summary>
        /// Clears all plugin translations.
        /// </summary>
        /// <returns>True if successful, false otherwise.</returns>
        public static bool Clear()
        {
            try
            {
                if (DZCPCore.Instance.ConfigType == DZCPCore.ConfigTypeDzcp.Default)
                {
                    SaveDefaultTranslation(string.Empty);
                    return true;
                }

                return DZCPBootstrap.DZCPPluginLoader.Plugins.All(plugin => SaveSeparatedTranslation(plugin.Prefix, string.Empty));
            }
            catch (Exception ex)
            {
                ServerConsole.AddLog($"[DZCP] Error clearing translations: {ex.Message}", ConsoleColor.Red);
                return false;
            }
        }
    }

    /// <summary>
    /// Interface for DZCP translations.
    /// </summary>
    public interface IDZCPTranslation
    {
        void CopyProperties(IDZCPTranslation source);
    }
}
          public class DZCPLoader
          {
              public DZCPLoader()
              {
                  // منشئ افتراضي
              }

              public DZCPLoader(string configPath)
              {
                  // منشئ مخصص بمعلمات
                  ConfigPath = configPath;
              }

              public string ConfigPath { get; set; }
          }