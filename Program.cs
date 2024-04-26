using System;
using System.Security.Cryptography;
using System.Text.Json;

namespace ArchiveCutter;
public class Program
{
    public const char cutterMode = 'c';
    public const char assembleMode = 'a';
    public const int bufferSize = 1048576; //1MB
    public static async Task Main(string[] args)
    {
        Console.WriteLine("Enter key ('c' - cutter mode | 'a' - assemble mode):");
        var modeChar = Console.ReadKey().KeyChar;
        if(modeChar == cutterMode)
        {
            Console.WriteLine("\n*Cutter Mode*\nEnter file path:");
            var filePath = Console.ReadLine()!;

            if (!File.Exists(filePath))
            {
                Console.WriteLine("File does not exist");
                return;
            }

            Console.WriteLine("Enter one part size in bytes:");
            var partSize = 0L;
            var inputPartSize = Console.ReadLine();

            while (!ulong.TryParse(inputPartSize, out var inputPartSizeResult) && inputPartSizeResult == 0 && inputPartSizeResult < long.MaxValue)
            {
                Console.WriteLine("Invalid value");
                inputPartSize = Console.ReadLine();
            }

            partSize = long.Parse(inputPartSize!);

            using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                var partIndex = 0;
                var IsFileEnd = false;
                var fileDirectory = Path.GetDirectoryName(filePath)!;
                var fileName = filePath.Substring(fileDirectory.Length+1, filePath.Length - fileDirectory.Length - 1);
                var partDirectory = Path.Combine(fileDirectory, $"{fileName}_Parts");
                var partCheckSums = new Dictionary<string, string>();

                Console.WriteLine($"Directory created: {partDirectory}");
                Directory.CreateDirectory(partDirectory);
                Console.WriteLine("Create parts:");

                while (!IsFileEnd)
                {
                    byte[] buffer = new byte[bufferSize];
                    long totalBytesRead = 0;
                    var partName = $"{fileName}.part{(partIndex != 0 ? partIndex.ToString() : string.Empty)}";
                    var partPath = Path.Combine(partDirectory, partName);

                    Console.WriteLine($"\t{partName}");

                    using (var partStream = new FileStream(partPath, FileMode.Create, FileAccess.Write)) 
                    { 
                        while (totalBytesRead < partSize)
                        {
                            int bytesRead = fileStream.Read(buffer, 0, bufferSize);

                            if (bytesRead == 0)
                            {
                                IsFileEnd = true;
                                break;
                            }

                            totalBytesRead += bytesRead;
                            partStream.Write(buffer, 0, bytesRead);
                        }

                        partStream.Close();
                    }

                    using (var cypherFunc = SHA512.Create())
                    {
                        using (var stream = File.OpenRead(partPath))
                        {
                            byte[] checksum = cypherFunc.ComputeHash(stream);
                            partCheckSums.Add(partName, BitConverter.ToString(checksum).Replace("-", string.Empty));
                        }
                    }

                    partIndex++;
                }

                var checksumFilePath = Path.Combine(partDirectory, $"{fileName}.part.checksum");
                File.Create(checksumFilePath).Close();
                var checksumJson = JsonSerializer.Serialize(partCheckSums, new JsonSerializerOptions() { WriteIndented = true });
                File.WriteAllText(checksumFilePath, checksumJson);

                fileStream.Close();
            }
        }
        if(modeChar == assembleMode)
        {
            Console.WriteLine("\nEnter part path without digit (*.part):");
            var firstPartPath = Console.ReadLine()!;

            if (string.IsNullOrEmpty(firstPartPath))
                return;

            var partsDirectory = Path.GetDirectoryName(firstPartPath)!;
            var directoryFiles = Directory.GetFiles(partsDirectory);
            var partFiles = directoryFiles.Where(f => f.Contains(firstPartPath) && f != $"{firstPartPath}.checksum");
            var partChecksumJson = File.ReadAllText(directoryFiles.First(f => f == $"{firstPartPath}.checksum"));
            var partChecksums = JsonSerializer.Deserialize<Dictionary<string, string>>(partChecksumJson)!;

            var filePath = firstPartPath.Substring(0, firstPartPath.Length - ".part".Length);
            using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                byte[] buffer = new byte[bufferSize];

                for(var partIndex = 0; partIndex < partFiles.Count(); partIndex++)
                {
                    var partPath = $"{firstPartPath}{(partIndex != 0 ? partIndex.ToString() : string.Empty)}";
                    var partName = partPath.Substring(partsDirectory.Length + 1, partPath.Length - partsDirectory.Length - 1);
                    var checksum = string.Empty;
                    Console.WriteLine($"Read part: {partName}");

                    using (var partStream = File.OpenRead(partPath))
                    {
                        using (var cypherFunc = SHA512.Create())
                        {
                            byte[] checksumBytes = cypherFunc.ComputeHash(partStream);
                            checksum = BitConverter.ToString(checksumBytes).Replace("-", string.Empty);

                            if (checksum != partChecksums[partName])
                            {
                                Console.WriteLine($"Invalid checksum ({partName})!");
                                fileStream.Close();
                                File.Delete(filePath);
                                return;
                            }
                        }
                    }

                    using (var partStream = File.OpenRead(partPath))
                    {
                        int bytesRead = -1;

                        while (bytesRead != 0)
                        {
                            bytesRead = partStream.Read(buffer, 0, bufferSize);
                            fileStream.Write(buffer, 0, bytesRead);
                        }
                    }
                }
            }
        }

        Console.WriteLine("Press any key for exit ...");
        Console.ReadKey();
    }
}
