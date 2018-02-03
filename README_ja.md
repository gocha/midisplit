MidiSplit
=========
[![AppVeyor Build Status](https://ci.appveyor.com/api/projects/status/fbysw0bkfuy18ubq/branch/master?svg=true)](https://ci.appveyor.com/project/gocha/midisplit/branch/master)

MIDI トラックをプログラムナンバー（楽器）ごとに分割します。

![MidiSplit のコンセプト](doc/assets/images/midisplit-concept.png)

MidiSplit はチャンネル数が制限されたシーケンス（例：レトロゲームの BGM）のトラックを再配置するのに効果的です。そのようなシーケンスはプログラムチェンジによって、1チャンネル（1トラック）で楽器を複数回変更します。MidiSplit は、あなたが使用されている楽器数を調べたり、各楽器の音量バランスを調整したりするのを支援します。

注意
------------------------

- MidiSplit はプログラムチェンジを発見した際にトラックを分割します。プログラムチェンジの前に配置されたノート以外のチャンネルメッセージ（コントロールチェンジなど）も新しいトラックに移されます。
    - `-cs` オプションによって、MidiSplit は切り替えポイントに他トラックで配置されたコントロールチェンジを複製することができます。
- 非チャンネルメッセージ（Sysex など）は入力トラックに残されます。
- リズムチャンネルは、メロディーチャンネルと同様に処理されます。
    - `-sp` オプションによって、MidiSplit はメロディーをノート単位でトラックに分割することができます。 (例: `-sp "ch10, prg127:0:1"`)
