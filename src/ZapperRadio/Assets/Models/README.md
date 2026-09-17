# YAMNet sound classifier

`yamnet.onnx` is [YAMNet](https://www.kaggle.com/models/google/yamnet/tensorFlow2/yamnet/1) by Google, a sound classifier trained on AudioSet (521 classes), converted to ONNX by [zeropointnine/yamnet-onnx](https://huggingface.co/zeropointnine/yamnet-onnx) (revision `ac2ca3bd45d12ec1f19f1144205ea529b4e9dedf`, SHA-256 `1510041dce24a2e9e84ec546807ac408ae496da6d1ed41bc3ccba649623f8e19`).

It is licensed under the Apache License 2.0, see `LICENSE-yamnet.txt`. The file is included unmodified.

ZapperRadio uses it to hear whether a stream plays music or speech (`Playback/SoundClassifier.cs`):

- input `waveform`: float32 `[samples]`, mono, 16 kHz
- output `output_0`: float32 `[frames, 521]`, class scores per 0.48 s frame (Speech = 0, Music = 132)
