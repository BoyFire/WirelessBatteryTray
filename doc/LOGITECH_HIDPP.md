# MX Vertical / Unifying HID++ 实机记录

当前 Unifying Receiver：`VID 046D / PID C52B`。Windows 枚举的 `MI_02` 中，`UsagePage FF00 / Usage 0001` 是 7 字节 HID++ 短报文 Collection，`UsagePage FF00 / Usage 0002` 是 20 字节长报文 Collection。向短报文 Collection 写入时，部分回复出现在长报文 Collection。

读取配对信息使用 HID++ Receiver Info 寄存器查询：

```text
TX 10 FF 83 B5 20 00 00
RX 11 FF 83 B5 20 07 08 40 7B 04 02 02 07 00 00 00 00 00 00 00
```

该回复标识槽位 1 的设备 WPID 为 `407B`，与 MX Vertical 对应。后续所有设备请求只发送给该槽位。

```text
ROOT.GetFeature(0x1004) -> index 0 (unsupported)
ROOT.GetFeature(0x1000) -> index 8
TX 10 01 08 08 00 00 00
RX 11 01 08 08 32 14 00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

电量 `0x32 = 50%`，状态 `0x00 = 未充电`。同一设备连续 4 次读取一致。充电状态其他值依据 [Solaar 的 HID++ 状态定义](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/common.py) 解析；本机尚未实测鼠标充电。

协议实现参考 [Solaar HID++ 传输](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/base.py)、[接收器配对信息读取](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/receiver.py) 和 [Battery Status Feature](https://github.com/pwr-Solaar/Solaar/blob/master/lib/logitech_receiver/hidpp20.py)。
