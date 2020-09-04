<img align="right" src="img/gemstone-wide-600.png" alt="gemstone logo">

# Threading
### GPA Gemstone Library

The Gemstone Threading Library organizes all Gemstone functionality related to threading.

[![GitHub license](https://img.shields.io/github/license/gemstone/threading?color=4CC61E)](https://github.com/gemstone/threading/blob/master/LICENSE)
[![Build status](https://ci.appveyor.com/api/projects/status/0eiilt0ki2s260yw?svg=true)](https://ci.appveyor.com/project/ritchiecarroll/threading)
![CodeQL](https://github.com/gemstone/threading/workflows/CodeQL/badge.svg)

This library includes helpful threading classes like the following:

* [ShortSynchronizedOperation](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_SynchronizedOperations_ShortSynchronizedOperation.htm):
  * Represents a short-running synchronized operation, i.e., an [Action](https://docs.microsoft.com/en-us/dotnet/api/system.action), that cannot run while it is already in progress. Specifically, the class has the ability to execute an operation on another thread or mark the operation as pending if the operation is already running. When an operation is pending and the current operation completes, the operation will run again, once, regardless of the number of requests to run. See [RunAsync](https://gemstone.github.io/threading/help/html/M_Gemstone_Threading_SynchronizedOperations_SynchronizedOperationBase_RunAsync.htm) and [TryRunAsync](https://gemstone.github.io/threading/help/html/M_Gemstone_Threading_SynchronizedOperations_SynchronizedOperationBase_TryRunAsync.htm) methods.
  * See also: [LongSynchronizedOperation](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_SynchronizedOperations_LongSynchronizedOperation.htm) and [DelayedSynchronizedOperation](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_SynchronizedOperations_DelayedSynchronizedOperation.htm).
* [Strand](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_Strands_Strand.htm):
  * Schedules tasks in a FIFO queue and executes them in a synchronized asynchronous loop.
* [PriorityStrand](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_Strands_PriorityStrand.htm):
  * Schedules tasks in a collection of FIFO queues and executes them in priority order.
* [PriorityQueue](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_Collections_PriorityQueue_1.htm):
  * Represents a thread-safe prioritized first in-first out (FIFO) collection.
* [ConcurrencyLimiter](https://gemstone.github.io/threading/help/html/T_Gemstone_Threading_ConcurrencyLimiter.htm):
  * Task scheduler that limits the number of tasks that can execute in parallel at any given time.

Among others.

### Documentation
[Full Library Documentation](https://gemstone.github.io/threading/help)
