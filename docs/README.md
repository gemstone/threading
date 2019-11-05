<img align="right" src="img/gemstone-wide-600.png" alt="gemstone logo">

# Threading
### GPA Gemstone Library

The Gemstone Threading Library organizes all Gemstone functionality related to threading.

[![GitHub license](https://img.shields.io/github/license/gemstone/threading?color=4CC61E)](https://github.com/gemstone/threading/blob/master/LICENSE)
[![Build status](https://ci.appveyor.com/api/projects/status/0eiilt0ki2s260yw?svg=true)](https://ci.appveyor.com/project/ritchiecarroll/threading)

This library includes helpful threading classes like the following:

* [ShortSynchronizedOperation](https://gemstone.github.io/threading/help/html/T_gemstone_threading_synchronizedoperations_ShortSynchronizedOperation.htm):
  * Represents a short-running synchronized operation, i.e., an [Action](https://docs.microsoft.com/en-us/dotnet/api/system.action), that cannot run while it is already in progress. Specifically, the class has the ability to execute an operation on another thread or mark the operation as pending if the operation is already running. When an operation is pending and the current operation completes, the operation will run again, once, regardless of the number of requests to run. See [RunAsync](https://gemstone.github.io/threading/help/html/M_gemstone_threading_synchronizedoperations_SynchronizedOperationBase_RunAsync.htm) and [TryRunAsync](https://gemstone.github.io/threading/help/html/M_gemstone_threading_synchronizedoperations_SynchronizedOperationBase_TryRunAsync.htm) methods.
  * See also: [LongSynchronizedOperation](https://gemstone.github.io/threading/help/html/T_gemstone_threading_synchronizedoperations_LongSynchronizedOperation.htm) and [DelayedSynchronizedOperation](https://gemstone.github.io/threading/help/html/T_gemstone_threading_synchronizedoperations_DelayedSynchronizedOperation.htm).
* [Strand](https://gemstone.github.io/threading/help/html/T_gemstone_threading_strands_Strand.htm):
  * Schedules tasks in a FIFO queue and executes them in a synchronized asynchronous loop.
* [PriorityStrand](https://gemstone.github.io/threading/help/html/T_gemstone_threading_strands_PriorityStrand.htm):
  * Schedules tasks in a collection of FIFO queues and executes them in priority order.
* [PriorityQueue](https://gemstone.github.io/threading/help/html/T_gemstone_threading_collections_PriorityQueue_1.htm):
  * Represents a thread-safe prioritized first in-first out (FIFO) collection.
* [ActionExtensions](https://gemstone.github.io/threading/help/html/T_gemstone_threading_extensions_ActionExtensions.htm):
  * Defines extension methods for actions, e.g., [DelayAndExecute](https://gemstone.github.io/threading/help/html/M_gemstone_threading_extensions_ActionExtensions_DelayAndExecute.htm) which executes an action on the thread pool after a specified number of milliseconds.
* [ConcurrencyLimiter](https://gemstone.github.io/threading/help/html/T_gemstone_threading_ConcurrencyLimiter.htm):
  * Task scheduler that limits the number of tasks that can execute in parallel at any given time.

Among others.

### Documentation
[Full Library Documentation](https://gemstone.github.io/threading/help)