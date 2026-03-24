## LOOP nodes and its types

### 1. Static for-loop
In this node, all the values required for running the loop will be defined in the workflow definition

#### Input properties
- start : (int) - starting value of this loop
- limit : (int) - maximum value for this loop to stop
- step : (int) - increment pace of the loop
- min-interval : (int) - minimum time in millis to wait before iterating to the next
    - default:1000
- ack : (bool) - false: fire and forget, true: wait for ack from the next node
    - opt:default:false
- ack-timeout : (int) - time in millis to stop waiting for acknoledgemnet
    - opt:default:0
- ack-all : (bool) - false: waits for any one of the conncetions to be acknoledged
    - opt:defalut:false


#### Ports
- input : signal : start
- input : signal : wait
- input : signal : continue
- input : signal : stop
- output : signal : onComplete
- output : signal : onStop
- output : data-signal : value - will be triggered on each iteration with the current value of the iteratable

#### Concurency
- Will be per workflow instance.
- 