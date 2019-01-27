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

        //GPU or CPU
        public bool GPU = true;

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
            OpenGLInit();
            StreamReader sr = new StreamReader( "../../samples/c4-orthogonal.rle" );
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
            if (!GPU)
            {
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
                //timer.Start();

                for (int i = 0; i < pw * ph; i++) pattern[i] = 0;

                var flags = ComputeMemoryFlags.UseHostPointer | ComputeMemoryFlags.ReadOnly;
                var pattern_d = new ComputeBuffer<uint>(context, flags, pattern);
                var second_d = new ComputeBuffer<uint>(context, flags, second);
                ComputeKernel kernel = program.CreateKernel("Simulate");
                kernel.SetMemoryArgument(0, pattern_d);
                kernel.SetMemoryArgument(1, second_d);
                kernel.SetValueArgument<uint>(2, pw);
                kernel.SetValueArgument<uint>(3, ph);

                //for (int i = 0; i < pw * ph; i++) second[i] = 0;

                ComputeEventList eventList = new ComputeEventList();

                long[] globalWorkSize = { pw*32*ph }; // 8 thread block (work groups)
                long[] localWorkSize = { 1 };   // of 128 work items (threads) each
                //queue.WriteToBuffer<uint>(pattern, pattern_d, false, eventList);
                queue.Execute(kernel, null, globalWorkSize, localWorkSize, eventList);
                queue.ReadFromBuffer<uint>(pattern_d, ref pattern, false, eventList);
                //queue.ReadFromBuffer<uint>(second_d, ref second, false, eventList);
                eventList.Wait();

                for (int i = 0; i < pw * ph; i++) second[i] = pattern[i];

                //while (Keyboard.GetState().IsKeyUp(Key.Space)) { }
                //while(Keyboard.GetState().IsKeyDown(Key.Space)) { }
            }
            //timer.Stop();
            /*
            var e = eventList[0];
            float elapsed = (e.FinishTime - e.StartTime) * 1.0e-6f; // nano -> milliseconds
            Console.WriteLine("Kernel execution time: {0} ms", elapsed);
            Console.WriteLine("Total execution time: {0} ms", timer.ElapsedMilliseconds);

            // Release resources
            for (int i = 0; i < eventList.Count; ++i)
            {
                eventList[i].Dispose();
            }
            eventList.Clear();
            //kernel.Dispose();
            //program.Dispose();
            //pattern_d.Dispose();
            //second_d.Dispose();*/
        }
        // TICK
        // Main application entry point: the template calls this function once per frame.
        public void Tick()
        {
            Console.WriteLine("generation " + generation++);
            // start timer
            timer.Restart();
            // run the simulation, 1 step
            Simulate();
            Console.WriteLine("Calculations : " + ": " + timer.ElapsedMilliseconds + "ms");
            timer2.Restart();
            // visualize current state
            screen.Clear( 0 );
            for( uint y = 0; y < screen.height; y++ ) for( uint x = 0; x < screen.width; x++ )
                if (GetBit( x + xoffset, y + yoffset ) == 1) screen.Plot( x, y, 0xffffff );
            // report performance
            Console.WriteLine( "Drawing : " + timer2.ElapsedMilliseconds + "ms" );
            Console.WriteLine();
        }

        public void OpenGLInit()
        {
            ComputePlatform platform = null;
            ComputeDevice device = null;

            for (int p = 0; p < ComputePlatform.Platforms.Count; ++p)
            {
                platform = ComputePlatform.Platforms[p];
                Console.WriteLine
                   ("Platform {0} \"{1}\" has {2} devices attached"
                   , p
                   , platform.Name
                   , platform.Devices.Count);

                for (int d = 0; d < platform.Devices.Count; d++)
                {
                    device = platform.Devices[d];
                    Console.WriteLine("  device [{0}]: {1}", d, device.Name);
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

            /*
            //Example data
            int len = 100;
            var xs_h = new float[len];
            var ys_h = new float[len];
            var zs_h = new float[len];

            for (int i = 0; i < len; i++)
            {
                xs_h[i] = i;
                ys_h[i] = len-1;
            }

            Stopwatch timer = new Stopwatch();
            timer.Start();

            var flags = ComputeMemoryFlags.UseHostPointer | ComputeMemoryFlags.ReadOnly;
            var xs_d = new ComputeBuffer<float>(context, flags, xs_h);
            var ys_d = new ComputeBuffer<float>(context, flags, ys_h);
            var zs_d = new ComputeBuffer<float>(context, ComputeMemoryFlags.None, len);

            ComputeKernel kernel = program.CreateKernel("GameOfLife");
            Console.WriteLine("info for kernel 'vector_add': ");
            Console.WriteLine("  workgroup size: " + kernel.GetWorkGroupSize(device));
            Console.WriteLine("  workgroup multiple: " + kernel.GetPreferredWorkGroupSizeMultiple(device));
            Console.WriteLine("  private memory: " + kernel.GetPrivateMemorySize(device));
            Console.WriteLine("  local memory: " + kernel.GetLocalMemorySize(device));
            Console.WriteLine();

            kernel.SetMemoryArgument(0, xs_d);
            kernel.SetMemoryArgument(1, ys_d);
            kernel.SetMemoryArgument(2, zs_d);
            kernel.SetValueArgument<int>(3, len);

            ComputeEventList eventList = new ComputeEventList();

            long[] globalWorkSize = { 1024 }; // 8 thread block (work groups)
            long[] localWorkSize = { 128 };   // of 128 work items (threads) each
            queue.Execute(kernel, null, globalWorkSize, localWorkSize, eventList);

            queue.ReadFromBuffer<float>(zs_d, ref zs_h, false, eventList);
            eventList.Wait();

            timer.Stop();

            // Print the results
            Console.WriteLine("Returned results:");
            for (int i = 0; i < len; ++i)
            {
                Console.WriteLine("{0} + {1} = {2}", xs_h[i], ys_h[i], zs_h[i]);
            }

            var e = eventList[0];
            float elapsed = (e.FinishTime - e.StartTime) * 1.0e-6f; // nano -> milliseconds
            Console.WriteLine("Kernel execution time: {0} ms", elapsed);
            Console.WriteLine("Total execution time: {0} ms", timer.ElapsedMilliseconds);

            // Release resources
            for (int i = 0; i < eventList.Count; ++i)
            {
                eventList[i].Dispose();
            }
            eventList.Clear();
            kernel.Dispose();
            program.Dispose();
            xs_d.Dispose();
            ys_d.Dispose();
            zs_d.Dispose();
            */
        }
    } // class Game
} // namespace Template

