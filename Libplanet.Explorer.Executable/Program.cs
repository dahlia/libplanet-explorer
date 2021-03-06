using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using Libplanet.Action;
using Libplanet.Explorer.Interfaces;
using Libplanet.Store;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Mono.Options;

namespace Libplanet.Explorer.Executable
{
    /// <summary>
    /// The program entry point to run a web server.
    /// </summary>
    public class Program
    {
        private static OptionSet options = new OptionSet
        {
            {
                "s|store-type=",
                "The storage backend to use.  Available types: " +
                    string.Join(", ", StoreRegistry.List()) + ".",
                v => storeTypeName = v
            },
            {
                "c|chain-id=",
                "The chain ID to view.  Omittable if there is only one ID.",
                v => chainId = v
            },
            {
                "h|help",
                "Show this message and exit.",
                v => showHelp = !(v is null)
            },
        };

        private static bool showHelp;

        private static string storeTypeName;

        private static string chainId;

        public static int Main(string[] args)
        {
            int code = Parse(args);
            if (code != 0)
            {
                return Math.Max(code, 0);
            }

            BuildWebHost().Run();
            return 0;
        }

        public static IWebHost BuildWebHost() =>
            WebHost.CreateDefaultBuilder()
                .UseStartup<ExplorerStartup<AppAgnosticAction, Startup>>()
                .Build();

        internal static int Parse(string[] args)
        {
            string programName = System.Environment.GetCommandLineArgs()[0];
            TextWriter stderr = Console.Error;
            List<string> extra;
            try
            {
                extra = options.Parse(args);
            }
            catch (OptionException e)
            {
                stderr.WriteLine("error: {0}", e.Message);
                stderr.WriteLine(
                    "Try `{0}' --help' for more information.",
                    programName
                );
                return 1;
            }

            if (showHelp)
            {
                Console.WriteLine(
                    "Usage: {0} [options] STORE_LOCATOR",
                    programName
                );
                Console.WriteLine();
                Console.WriteLine("Options:");
                options.WriteOptionDescriptions(Console.Out);
                return -1;
            }

            if (extra.Count > 1)
            {
                stderr.WriteLine("error: Too many arguments.");
                return 1;
            }
            else if (extra.Count < 1)
            {
                stderr.WriteLine("error: Too few arguments.");
                return 1;
            }

            IStore store;
            try
            {
                store = StoreRegistry.Get(storeTypeName ?? "file", extra[0]);
            }
            catch (StoreRegistry.StoreNotFoundException e)
            {
                stderr.WriteLine(
                    "error: Invalid -s/--store-type: `{0}'.",
                    e.TypeName
                );
                stderr.WriteLine(
                    "Available types are: {0}.",
                    string.Join(", ", StoreRegistry.List())
                );
                return 1;
            }

            if (chainId is null)
            {
                string[] nsList = store.ListNamespaces().Take(2).ToArray();
                if (nsList.Length > 1)
                {
                    stderr.WriteLine("error: There are multiple chain IDs.");
                    stderr.WriteLine("Explicitly choose a -c/--chain-id.");
                    return 1;
                }
                else if (nsList.Length < 1)
                {
                    stderr.WriteLine("error: There are no chain IDs.");
                    stderr.WriteLine("Explicitly choose a -c/--chain-id.");
                    return 1;
                }

                chainId = nsList[0];
            }

            try
            {
                Startup.ChainIdState = Guid.Parse(chainId);
            }
            catch (FormatException)
            {
                stderr.WriteLine("error: {0} is not a valid UUID.", chainId);
                return 1;
            }

            Startup.StoreState = store;
            return 0;
        }

        internal class AppAgnosticAction : IAction
        {
            public IImmutableDictionary<string, object> PlainValue
            {
                get;
                private set;
            }

            public void LoadPlainValue(
                IImmutableDictionary<string, object> plainValue)
            {
                PlainValue = plainValue;
            }

            public IAccountStateDelta Execute(IActionContext context)
            {
                return context.PreviousStates;
            }
        }

        internal class Startup : IBlockchainStore
        {
            public IStore Store => StoreState;

            public Guid ChainId => ChainIdState;

            internal static IStore StoreState { get; set; }

            internal static Guid ChainIdState { get; set; }
        }
    }
}
