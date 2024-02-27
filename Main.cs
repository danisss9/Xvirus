using System;
using System.IO;
using static XescSDK.XvirusSDK;

namespace XvirusCLI
{
    public class XvirusCLI
    {
        public static void Main(string[] args)
        {
            if(args.Length < 1)
            {
                Console.WriteLine("Invalid command");
                return;
            }
                
            var isInteractive = false;
            do
            {
                switch (args[0].ToLower())
                {
                    case "l":
                    case "load":
                        Load(args.Length > 1 && args[1].ToLower() == "true");
                        Console.WriteLine("Load successful");
                        break;
                    case "u":
                    case "unload":
                        Unload();
                        Console.WriteLine("Unload successful");
                        break;
                    case "s":
                    case "scan":
                        if (args.Length < 2)
                            Console.WriteLine("Invalid path");
                        else
                            Console.WriteLine(ScanString(args[1]));
                        break;
                    case "sf":
                    case "scanFolder":
                        if (args.Length < 2 || !Directory.Exists(args[1]))
                            Console.WriteLine("Invalid folder path");
                        else
                            Console.WriteLine(ScanFolderString(args[1]));
                        break;
                    case "up":
                    case "update":
                        Console.WriteLine(CheckUpdates(args.Length > 1 && args[1].ToLower() == "true", args.Length > 2 && args[2].ToLower() == "true"));
                        break;
                    case "st":
                    case "settings":
                        Console.WriteLine(GetSettingsString());
                        break;
                    case "log":
                    case "logging":
                        if (args.Length < 2)
                            Console.WriteLine(Logging());
                        else
                            Console.WriteLine(Logging(args[1].ToLower() == "true"));
                        break;
                    case "bf":
                    case "basefolder":
                        if (args.Length < 2)
                            Console.WriteLine(BaseFolder());
                        else
                            Console.WriteLine(BaseFolder(args[1]));
                        break;
                    case "v":
                    case "version":
                        Console.WriteLine(Version());
                        break;
                    case "i":
                    case "interactive":
                        isInteractive = true;
                        break;
                    case "q":
                    case "quit":
                        isInteractive = false;
                        break;
                    default:
                        Console.WriteLine("Invalid command.");
                        break;
                }

                if (isInteractive)
                    args = Console.ReadLine().Split(" ", 2);

            } while (isInteractive);
        }
    }
}
