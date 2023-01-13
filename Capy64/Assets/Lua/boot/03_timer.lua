local timer = require("timer")
local expect = require("expect").expect
local range = require("expect").range

function timer.sleep(n)
    expect(1, n, "number")
    range(1, 1)

    local timerId = timer.start(n)
    repeat
        local _, par = coroutine.yield("timer")
    until par == timerId
end