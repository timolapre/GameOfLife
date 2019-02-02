using System;
using System.IO;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using Cloo;
using System.Windows;

namespace Template
{
    class Game
    {
        // screen surface to draw to
        public Surface screen;
        // stopwatch
        Stopwatch timer = new Stopwatch();
        Stopwatch timer2 = new Stopwatch();
        int generation = 0;
        // two buffers for the pattern: simulate reads 'second', writes to 'pattern'
        uint [] pattern;
        uint [] second;
        uint pw, ph; // note: pw is in uints; width in bits is 32 this value.
        // helper function for setting one bit in the pattern buffer
        void BitSet(uint x, uint y){pattern[y * pw + (x >> 5)] |= 1U << (int)(x & 31);}
        // helper function for getting one bit from the secondary pattern buffer
        uint GetBit( uint x, uint y ){return (second[y * pw + (x >> 5)] >> (int)(x & 31)) & 1U;}
        // mouse handling: dragging functionality
        uint xoffset = 0, yoffset = 0;
        bool lastLButtonState = false;
        int dragXStart, dragYStart, offsetXStart, offsetYStart;

        //OpenCL stuff
        ComputeContext context;
        ComputeProgram program;
        ComputeCommandQueue queue;
        ComputeKernel kernel;

        //GPU or CPU
        public bool GPU = true;
        public bool Wrap = true;
        public string Pattern = "c4-orthogonal";
        public bool ShowDebug = true;

        public void SetMouseState( int x, int y, bool pressed )
        {
            if (pressed)
            {
                if (lastLButtonState)
                {
                    int deltax = x - dragXStart, deltay = y - dragYStart;
                    xoffset = (uint)Math.Min( pw * 32 - screen.width, Math.Max( 0, offsetXStart - deltax ) );
                    yoffset = (uint)Math.Min( ph - screen.height, Math.Max( 0, offsetYStart - deltay ) );
                }
                else
                {
                    dragXStart = x;
                    dragYStart = y;
                    offsetXStart = (int)xoffset;
                    offsetYStart = (int)yoffset;
                    lastLButtonState = true;
                }
            }
            else lastLButtonState = false;
        }
        // minimalistic .rle file reader for Golly files (see http://golly.sourceforge.net)
        public void Init()
        {
            //The Console questions like run on GPU/CPU? Wrap/No Wrap? etc
            ConsoleQuestions();
            //Initialize OpenGL stuff (everything that needs to be done once)
            OpenGLInit();

            StreamReader sr = new StreamReader("../../samples/" + Pattern + ".rle");
            uint state = 0, n = 0, x = 0, y = 0;
            while (true)
            {
                String line = sr.ReadLine();
                if (line == null) break; // end of file
                int pos = 0;
                if (line[pos] == '#') continue; /* comment line */ else if (line[pos] == 'x') // header
                {
                    String[] sub = line.Split( new char[] { '=',',' }, StringSplitOptions.RemoveEmptyEntries );
                    pw = (UInt32.Parse( sub[1] ) + 31) / 32;
                    ph = UInt32.Parse( sub[3] );
                    pattern = new uint[pw * ph];
                    second = new uint[pw * ph];
                }
                else while (pos < line.Length)
                {
                    Char c = line[pos++];
                    if (state == 0) if (c < '0' || c > '9') { state = 1; n = Math.Max( n, 1 ); } else n = (uint)(n * 10 + (c - '0'));
                    if (state == 1) // expect other character
                    {
                        if (c == '$') { y += n; x = 0; } // newline
                        else if (c == 'o') for( int i = 0; i < n; i++ ) BitSet( x++, y ); else if (c == 'b') x += n;
                        state = n = 0;
                    }
                }
            }

            // swap buffers
            for ( int i = 0; i < pw * ph; i++ ) second[i] = pattern[i];
        }
        // SIMULATE
        // Takes the pattern in array 'second', and applies the rules of Game of Life to produce the next state
        // in array 'pattern'. At the end, the result is copied back to 'second' for the next generation.
        void Simulate()
        {
            //Run on GPU or CPU
            if (!GPU)
            {
                // Code for CPU

                // clear destination pattern
                for (int i = 0; i < pw * ph; i++) pattern[i] = 0;
                // process all pixels, skipping one pixel boundary
                uint w = pw * 32, h = ph;
                for (uint y = 1; y < h - 1; y++) for (uint x = 1; x < w - 1; x++)
                    {
                        // count active neighbors
                        uint n = GetBit(x - 1, y - 1) + GetBit(x, y - 1) + GetBit(x + 1, y - 1) + GetBit(x - 1, y) +
                            GetBit(x + 1, y) + GetBit(x - 1, y + 1) + GetBit(x, y + 1) + GetBit(x + 1, y + 1);
                        if ((GetBit(x, y) == 1 && n == 2) || n == 3) BitSet(x, y);
                    }
                // swap buffers
                for (int i = 0; i < pw * ph; i++) second[i] = pattern[i];
            }
            else
            {
                //Code for GPU

                var flags = ComputeMemoryFlags.UseHostPointer | ComputeMemoryFlags.ReadOnly;
                var pattern_d = new ComputeBuffer<uint>(context, flags, pattern);
                var second_d = new ComputeBuffer<uint>(context, flags, second);
                if (Wrap)
                {
                    //When Wrap is on
                    kernel = program.CreateKernel("SimulateWrap");
                }
                else
                {
                    //When Wrap is off
                    kernel = program.CreateKernel("Simulate");
                }
                kernel.SetMemoryArgument(0, pattern_d);
                kernel.SetMemoryArgument(1, second_d);
                kernel.SetValueArgument<uint>(2, pw);
                kernel.SetValueArgument<uint>(3, ph);

                ComputeEventList eventList = new ComputeEventList();

                long[] globalWorkSize = { pw*32*ph };
                long[] localWorkSize = { 32 };
                queue.Execute(kernel, null, globalWorkSize, localWorkSize, eventList);
                queue.ReadFromBuffer<uint>(pattern_d, ref second, false, eventList);
                eventList.Wait();

            }
        }

        // TICK
        // Main application entry point: the template calls this function once per frame.
        public void Tick()
        {
            if(ShowDebug) Console.WriteLine("generation " + generation++);
            // start timer
            timer.Restart();
            // run the simulation, 1 step
            Simulate();
            if (ShowDebug) Console.WriteLine("Calculations : " + ": " + timer.ElapsedMilliseconds + "ms");
            timer2.Restart();
            // visualize current state
            screen.Clear( 0 );
            for( uint y = 0; y < screen.height; y++ ) for( uint x = 0; x < screen.width; x++ )
                if (GetBit( x + xoffset, y + yoffset ) == 1) screen.Plot( x, y, 0xffffff );
            // report performance
            if (ShowDebug) Console.WriteLine( "Drawing : " + timer2.ElapsedMilliseconds + "ms" );
            if (ShowDebug) Console.WriteLine();
        }

        //Console Questions
        public void ConsoleQuestions()
        {
            CPUorGPU();
            Console.WriteLine();
            if (GPU)
            {
                WrapOrNot();
                Console.WriteLine();
            }
            WhatPattern();
            Console.WriteLine();
            DebugMode();
            Console.WriteLine();
            Console.WriteLine("Ok cool! Let's run the program!");
            Console.WriteLine();
        }

        public void CPUorGPU()
        {
            Console.WriteLine("Run the program on the CPU or the GPU?");
            string hardware = Console.ReadLine();
            if (hardware.ToLower() == "cpu") GPU = false;
            else if (hardware.ToLower() == "gpu") GPU = true;
            else
            {
                Console.WriteLine("I did not understand");
                Console.WriteLine();
                CPUorGPU();
            }
        }

        public void WrapOrNot()
        {
            Console.WriteLine("Do you want the program to wrap around the edges? (y/n)");
            string wrap = Console.ReadLine();
            if (wrap == "y" || wrap == "yes") Wrap = true;
            else if (wrap == "n" || wrap == "no") Wrap = false;
            else
            {
                Console.WriteLine("I did not understand");
                Console.WriteLine();
                WrapOrNot();
            }
        }

        public void WhatPattern()
        {
            Console.WriteLine("What pattern do you want to use? (Pick a number)");
            Console.WriteLine("(1) c4-orthogonal");
            Console.WriteLine("(2) turing_js_r");
            Console.WriteLine("(3) Breeder");
            Console.WriteLine("(4) PrimeCalculator");
            Console.WriteLine("(5) RakeP90");
            string pattern = Console.ReadLine();
            if (pattern == "1") Pattern = "c4-orthogonal";
            else if (pattern == "2") Pattern = "turing_js_r";
            else if (pattern == "3") Pattern = "Breeder";
            else if (pattern == "4") Pattern = "PrimeCalc";
            else if (pattern == "5") Pattern = "RakeP90";
            else
            {
                Console.WriteLine(pattern + " is not an existing map");
                Console.WriteLine();
                WhatPattern();
            }
        }

        public void DebugMode()
        {
            Console.WriteLine("Do you want the terminal to show extra info like generation number and frame time calculations (y/n)");
            string debug = Console.ReadLine();
            if (debug == "y" || debug == "yes") ShowDebug = true;
            else if (debug == "n" || debug == "no") ShowDebug = false;
            else
            {
                Console.WriteLine("I did not understand");
                Console.WriteLine();
                DebugMode();
            }
        }

        //Initializing everything OpenGL related
        public void OpenGLInit()
        {
            ComputePlatform platform = null;
            ComputeDevice device = null;

            for (int p = 0; p < ComputePlatform.Platforms.Count; ++p)
            {
                platform = ComputePlatform.Platforms[p];
                /*Console.WriteLine
                   ("Platform {0} \"{1}\" has {2} devices attached"
                   , p
                   , platform.Name
                   , platform.Devices.Count);*/

                for (int d = 0; d < platform.Devices.Count; d++)
                {
                    device = platform.Devices[d];
                    //Console.WriteLine("  device [{0}]: {1}", d, device.Name);
                }
            }

            context = new ComputeContext(
              ComputeDeviceTypes.Gpu,
              new ComputeContextPropertyList(platform),
              null,
              IntPtr.Zero);

            queue = new ComputeCommandQueue
              (context
              , device
              , ComputeCommandQueueFlags.Profiling      // enable event timing
              );

            StreamReader reader = new StreamReader("../../GameOfLife.cl");
            string source = reader.ReadToEnd();
            reader.Close();
            //Console.WriteLine(source);

            program = new ComputeProgram(context, source);
            try
            {
                program.Build(null, null, null, IntPtr.Zero);
            }
            catch
            {
                Console.WriteLine(program.GetBuildLog(device));   // error log
                System.Environment.Exit(7);
            }
            Console.WriteLine(program.GetBuildLog(device));     // warnings
        }
    } // class Game
} // namespace Template

