using System;
using System.Threading;
using System.Threading.Tasks;
using Alife.Speech;

namespace Alife.Speech.App;

class Program
{
    static async Task Main(string[] args)
    {
        try
        {
            Console.WriteLine("Initializing Speech Recognition (Vosk) and Synthesis (Edge)...");

            // The model is located in the output folder (bin/Debug/...)
            // We use AppDomain.CurrentDomain.BaseDirectory to find it regardless of how we are launched.
            string modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "model"); 

            using var recognizer = new LocalSpeechRecognizer(modelPath);
            using var synthesizer = new LocalSpeechSynthesizer();

            CancellationTokenSource? currentTtsCts = null;
            bool isSpeaking = false;

            recognizer.SpeechHypothesized += (s, text) =>
            {
                if (!isSpeaking)
                {
                    // Print partial results on the same line to show "activity"
                    Console.Write($"\r[Hearing...]: {text}        ");
                }
            };

            recognizer.SpeechRecognized += async (s, e) =>
            {
                var (text, confidence) = e;
                // Clear the "Hearing" line
                Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
                Console.WriteLine($"[STT Log] Text: \"{text}\", Confidence: {confidence:P1}");

                // If we are currently speaking, we only allow high-confidence interruptions
                if (isSpeaking && confidence < 0.6)
                {
                    Console.WriteLine("[STT Log] Interruption ignored: Confidence too low (likely echo).");
                    return;
                }

                Console.WriteLine($"[User]: {text}");
                
                // If the user interrupted by speaking, immediately cancel any ongoing TTS
                synthesizer.Stop();
                currentTtsCts?.Cancel();
                
                // Simulate AI responding in a "little girlfriend" style
                string reply = $"哎呀，人家听到你说 {text} 啦！嘿嘿，主人是不是又在想我了呀？我一直在听着呢，只要你说话，我就会陪着你哒！";
                Console.WriteLine($"[System]: {reply}");
                
                currentTtsCts = new CancellationTokenSource();
                isSpeaking = true;
                try
                {
                    await synthesizer.SpeakAsync(reply, currentTtsCts.Token);
                    Console.WriteLine("[System]: (Finished speaking)");
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine("[System TTS Interrupted by User]");
                }
                finally
                {
                    isSpeaking = false;
                }
            };

            Console.WriteLine("Ready. Start speaking into your microphone! (Press Ctrl+C to exit)");
            
            // Start continuous listening
            recognizer.StartListening();

            // Keep the application running indefinitely
            await Task.Delay(Timeout.Infinite);
        }
        catch (Exception ex)
        {
            Console.WriteLine("\n[CRASH]: " + ex.Message);
            Console.WriteLine(ex.StackTrace);
        }
    }
}
