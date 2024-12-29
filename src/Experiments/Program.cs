using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Experiments
{
    /// <summary>
    /// A "Box" where one thread can put things in it (are indicate that it is finished). A second thread can
    /// try to take them out of the box. Importantly, each thread is blocked until the hand-off happens. I.e.,
    /// the putting thread will block until a getting thread arrives to take its value, and vice-versa.
    /// 
    /// The Put method takes a Func to allow a maximally lazy operation. I.e., it can delay even computing the
    /// value that will be placed in the box until the getter is available.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class Box<T>
    {
        private T value;
        private bool hasValue;
        private readonly AutoResetEvent 
            producerAvailable = new AutoResetEvent(false), 
            consumerAvailable = new AutoResetEvent(false), 
            dataAvailable = new AutoResetEvent(false);

        public void Put(IEnumerator<T> e) // need a Put() that is thread-safe
        {
            producerAvailable.Set(); // the problem is that multiple Puts generate multiple Sets and probably later ones are lost
            Console.WriteLine("Box.Put: Just called producerAvailable.Set");
            consumerAvailable.WaitOne();

            hasValue = e.MoveNext();
            Console.WriteLine($"Box.Put: set hasValue={hasValue}");
            if (hasValue)
            {
                value = e.Current;
            }

            dataAvailable.Set();
        }

        /// <summary>
        /// Put a value in the box.
        /// </summary>
        /// <param name="src">Generator for the value to put in the box. Pass null to indicate there are no more value.</param>
        //public void Put(Func<T> src)
        //{
        //    barrier.SignalAndWait();

        //    if (src is null)
        //    {
        //        hasValue = false;
        //    }
        //    else
        //    {
        //        hasValue = true;
        //        value = src();
        //    }

        //    barrier2.SignalAndWait();
        //}

        //public bool TryGet(out T t)
        //{
        //    barrier.SignalAndWait();
        //    barrier2.SignalAndWait();
        //    t = value;
        //    return hasValue;
        //}

        public IEnumerator<T> GetEnumerator()
        {
            try
            {
                while (true)
                {
                    consumerAvailable.Set();
                    if (!producerAvailable.WaitOne(2000))
                    {
                        Console.WriteLine("producer never became available!");
                        throw new Exception();
                    }
                    if (!dataAvailable.WaitOne(2000))
                    {
                        Console.WriteLine("data never became available!");
                        throw new Exception();
                    }
                    Console.WriteLine($"Box.GetEnumerator: hasValue={hasValue}");
                    if (hasValue)
                    {
                        Console.WriteLine($"Box.GetEnumerator: yielding {value}");
                        yield return value;
                    }
                    else
                    {
                        Console.WriteLine("Box.GetEnumerator: yielding break");
                        yield break;
                    }
                }
            }
            finally
            {
                Console.WriteLine("Box.GetEnumerator: Disposing wait handles");
                consumerAvailable.Dispose();
                producerAvailable.Dispose();
                dataAvailable.Dispose();
            }
        }
    }

    class Program
    {
        static IEnumerable<int> DoWork()
        {
            Console.WriteLine("Computing 42");
            yield return 42;
            Console.WriteLine("Computing 99");
            yield return 99;
            Console.WriteLine("Work done");
        }
        static void Main()
        {
            /* Definition of a zero-capacity BlockingCollection (ZCBC). Initially Add(x) blocks.
             * When eble = GetConsumingEnumerable() is called nothing happens. When etor = eble.GetEnumerator() is called
             * nothing happens. Calling etor.MoveNext() causes Add to unblock and returns true. Calling etor.Current blocks
             * until Add completes and returns x. Calling etor.MoveNext() blocks until either Add is called on the ZCBC,
             * or CompleteAdding() is called on the ZCBC.
             */
            //Console.WriteLine("DoWork(");
            //var eble = DoWork();
            //Console.WriteLine("); GetEnumerator(");
            //var etor = eble.GetEnumerator();
            //Console.WriteLine("); MoveNext(");
            //Console.WriteLine($")={etor.MoveNext()}; Current(");
            //Console.WriteLine($")={etor.Current}; MoveNext(");
            //Console.WriteLine($")={etor.MoveNext()}; Current(");
            //Console.WriteLine($")={etor.Current}; MoveNext(");
            //Console.WriteLine($")={etor.MoveNext()}; Dispose(");
            //etor.Dispose();
            //Console.WriteLine(")");

            var box = new Box<int>();

            var producer = Task.Run(() =>
            {
                Console.WriteLine("Producer: start");
                Console.WriteLine("Producer: Put(42 ...");
                box.Put(Just(42));
                Console.WriteLine("Producer:); Put(43 ...");
                box.Put(Just(43));
                Console.WriteLine("Producer: ); Put(Nothing() ...");
                Thread.Sleep(500);
                box.Put(Nothing());
                Console.WriteLine("Producer: ); done.");
            });

            var producer2 = Task.Run(() =>
            {
                Console.WriteLine("Producer2: start");
                Console.WriteLine("Producer2: Put(17 ...");
                box.Put(Just(17));
                Console.WriteLine("Producer2: ); Done");
                //box.Put(Nothing());
                //Console.WriteLine("Producer2: ); done.");
            });

            static IEnumerator<int> Just(int n)
            {
                Console.WriteLine($"Just: about to make {n}");
                yield return n;
                Console.WriteLine("Just: done");
            }
            static IEnumerator<int> Nothing() { yield break; }

            Console.WriteLine("Main: Start sleep");
            Thread.Sleep(1000);
            Console.WriteLine("Main: end sleep");

            var consumer = Task.Run(() =>
            {
                Console.WriteLine("Consumer: start");
                foreach (int x in box)
                {
                    Console.WriteLine($"Consumer: got {x}");
                }
                Console.WriteLine("Consumer: done");
            });

            Task.WaitAll(consumer, producer, producer2);
            Console.WriteLine("Main: All tasks completed");
        }
    }
}
