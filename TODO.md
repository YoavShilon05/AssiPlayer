# bug-fixes
- radio tries to play if no members in call are connected
- if two servers are downloading the same song, the first server to finish playing it will delete the file from existence and the other server won't hear it.
- connection is zombie
- maybe downloading things doesn't work if the file already exists

# code
- optimize radio
- refactor BigDic

# features
- play a playlist (and shuffle it)
- shuffle an artist's most popular songs
- search songs on genius
- apple music
- streamline connection
- how much did you like the song that just played
- blacklist users

# responsiveness
- if you skip a lot and the bot starts to lag say something about it

# radio algorithms
- explore - play genres that people in the call don't really listen to
- uncharted - play stuff that is in your liked songs but you don't listen to a lot.
- rework BalanceWeights()

# publishing
- hide tokens in .txt
- document code
- name and picture
