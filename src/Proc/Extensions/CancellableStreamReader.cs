using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ProcNet.Extensions
{
	/// <summary>
	/// A streamreader implementation that supports CancellationToken inside it's ReadAsync method allowing it to not block indefintely.
	/// </summary>
	internal class CancellableStreamReader : StreamReader
	{
		public CancellableStreamReader(Stream stream, Encoding encoding, bool detectEncodingFromByteOrderMarks, int bufferSize, bool leaveOpen, CancellationToken token)
			: base(stream, encoding, detectEncodingFromByteOrderMarks, bufferSize, leaveOpen)
		{
			_stream = stream;
			_encoding = encoding;
			_decoder = encoding.GetDecoder();
			if (bufferSize < MinBufferSize) bufferSize = MinBufferSize;
			_byteBuffer = new byte[bufferSize];
			_maxCharsPerBuffer = encoding.GetMaxCharCount(bufferSize);
			_charBuffer = new char[_maxCharsPerBuffer];
			_byteLen = 0;
			_bytePos = 0;
			_detectEncoding = detectEncodingFromByteOrderMarks;
			_token = token;
			_preamble = encoding.GetPreamble();
			_checkPreamble = (_preamble.Length > 0);
			_isBlocked = false;
			_closable = !leaveOpen;
		}
		private const int MinBufferSize = 128;

		private Stream _stream;
		private Encoding _encoding;
		private Decoder _decoder;
		private byte[] _byteBuffer;
		private char[] _charBuffer;
		private byte[] _preamble;   // Encoding's preamble, which identifies this encoding.
		private int _charPos;
		private int _charLen;
		// Record the number of valid bytes in the byteBuffer, for a few checks.
		private int _byteLen;
		// This is used only for preamble detection
		private int _bytePos;

		// This is the maximum number of chars we can get from one call to
		// ReadBuffer.  Used so ReadBuffer can tell when to copy data into
		// a user's char[] directly, instead of our internal char[].
		private int _maxCharsPerBuffer;

		// We will support looking for byte order marks in the stream and trying
		// to decide what the encoding might be from the byte order marks, IF they
		// exist.  But that's all we'll do.
		private bool _detectEncoding;
		private readonly CancellationToken _token;

		// Whether we must still check for the encoding's given preamble at the
		// beginning of this file.
		private bool _checkPreamble;

		// Whether the stream is most likely not going to give us back as much
		// data as we want the next time we call it.  We must do the computation
		// before we do any byte order mark handling and save the result.  Note
		// that we need this to allow users to handle streams used for an
		// interactive protocol, where they block waiting for the remote end
		// to send a response, like logging in on a Unix machine.
		private bool _isBlocked;

		// The intent of this field is to leave open the underlying stream when
		// disposing of this StreamReader.  A name like _leaveOpen is better,
		// but this type is serializable, and this field's name was _closable.
		private bool _closable;  // Whether to close the underlying stream.

		public override Task<int> ReadAsync(char[] buffer, int index, int count)
		{
			if (buffer==null) throw new ArgumentNullException(nameof(buffer));
			if (index < 0 || count < 0) throw new ArgumentOutOfRangeException((index < 0 ? "index" : "count"));
			if (buffer.Length - index < count) throw new ArgumentException(nameof(buffer));

			return ReadAsyncInternal(buffer, index, count);
		}

		public async Task<bool> EndOfStreamAsync()
		{
			if (_stream == null)
				throw new ArgumentNullException(nameof(_stream));

			if (_charPos < _charLen) return false;

			// This may block on pipes!
			var numRead = await ReadBufferAsync();
			return numRead == 0;
		}

		private async Task<int> ReadBufferAsync()
		{
			_charLen = 0;
			_charPos = 0;
			var tmpByteBuffer = _byteBuffer;
			var tmpStream = _stream;

			if (!_checkPreamble) _byteLen = 0;
			do
			{
				if (_checkPreamble)
				{
					var tmpBytePos = _bytePos;
					var len = await tmpStream.ReadAsync(tmpByteBuffer, tmpBytePos, tmpByteBuffer.Length - tmpBytePos, _token).ConfigureAwait(false);

					if (len == 0)
					{
						// EOF but we might have buffered bytes from previous
						// attempt to detect preamble that needs to be decoded now
						if (_byteLen <= 0) return _charLen;
						_charLen += _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, _charLen);
						// Need to zero out the _byteLen after we consume these bytes so that we don't keep infinitely hitting this code path
						_bytePos = 0;
						_byteLen = 0;

						return _charLen;
					}

					_byteLen += len;
				}
				else
				{
					_byteLen = await tmpStream.ReadAsync(tmpByteBuffer, 0, tmpByteBuffer.Length, _token).ConfigureAwait(false);

					if (_byteLen == 0) // We're at EOF
						return _charLen;
				}

				// _isBlocked == whether we read fewer bytes than we asked for.
				// Note we must check it here because CompressBuffer or
				// DetectEncoding will change _byteLen.
				_isBlocked = (_byteLen < tmpByteBuffer.Length);

				// Check for preamble before detect encoding. This is not to override the
				// user suppplied Encoding for the one we implicitly detect. The user could
				// customize the encoding which we will loose, such as ThrowOnError on UTF8
				if (IsPreamble())
					continue;

				// If we're supposed to detect the encoding and haven't done so yet,
				// do it.  Note this may need to be called more than once.
				if (_detectEncoding && _byteLen >= 2)
					DetectEncoding();

				_charLen += _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, _charLen);
			} while (_charLen == 0);

			return _charLen;
		}


		internal async Task<int> ReadAsyncInternal(char[] buffer, int index, int count)
		{
			if (_charPos == _charLen && (await ReadBufferAsync().ConfigureAwait(false)) == 0)
				return 0;

			var charsRead = 0;

			// As a perf optimization, if we had exactly one buffer's worth of
			// data read in, let's try writing directly to the user's buffer.
			var readToUserBuffer = false;

			var tmpByteBuffer = _byteBuffer;
			var tmpStream = _stream;

			while (count > 0)
			{
				// n is the cha----ters avaialbe in _charBuffer
				var n = _charLen - _charPos;

				// charBuffer is empty, let's read from the stream
				if (n == 0)
				{
					_charLen = 0;
					_charPos = 0;

					if (!_checkPreamble)
						_byteLen = 0;

					readToUserBuffer = count >= _maxCharsPerBuffer;

					// We loop here so that we read in enough bytes to yield at least 1 char.
					// We break out of the loop if the stream is blocked (EOF is reached).
					do
					{
						if (_checkPreamble)
						{
							var tmpBytePos = _bytePos;
							var len = await tmpStream.ReadAsync(tmpByteBuffer, tmpBytePos, tmpByteBuffer.Length - tmpBytePos, _token).ConfigureAwait(false);

							if (len == 0)
							{
								// EOF but we might have buffered bytes from previous
								// attempts to detect preamble that needs to be decoded now
								if (_byteLen > 0)
								{
									if (readToUserBuffer)
									{
										n = _decoder.GetChars(tmpByteBuffer, 0, _byteLen, buffer, index + charsRead);
										_charLen = 0; // StreamReader's buffer is empty.
									}
									else
									{
										n = _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, 0);
										_charLen += n; // Number of chars in StreamReader's buffer.
									}
								}

								_isBlocked = true;
								break;
							}
							else
							{
								_byteLen += len;
							}
						}
						else
						{
							_byteLen = await tmpStream.ReadAsync(tmpByteBuffer, 0, tmpByteBuffer.Length, _token).ConfigureAwait(false);

							if (_byteLen == 0) // EOF
							{
								_isBlocked = true;
								break;
							}
						}

						// _isBlocked == whether we read fewer bytes than we asked for.
						// Note we must check it here because CompressBuffer or
						// DetectEncoding will change _byteLen.
						_isBlocked = (_byteLen < tmpByteBuffer.Length);

						// Check for preamble before detect encoding. This is not to override the
						// user suppplied Encoding for the one we implicitly detect. The user could
						// customize the encoding which we will loose, such as ThrowOnError on UTF8
						// Note: we don't need to recompute readToUserBuffer optimization as IsPreamble
						// doesn't change the encoding or affect _maxCharsPerBuffer
						if (IsPreamble())
							continue;

						// On the first call to ReadBuffer, if we're supposed to detect the encoding, do it.
						if (_detectEncoding && _byteLen >= 2)
						{
							DetectEncoding();
							// DetectEncoding changes some buffer state.  Recompute this.
							readToUserBuffer = count >= _maxCharsPerBuffer;
						}


						_charPos = 0;
						if (readToUserBuffer)
						{
							n += _decoder.GetChars(tmpByteBuffer, 0, _byteLen, buffer, index + charsRead);

							_charLen = 0; // StreamReader's buffer is empty.
						}
						else
						{
							n = _decoder.GetChars(tmpByteBuffer, 0, _byteLen, _charBuffer, 0);


							_charLen += n; // Number of chars in StreamReader's buffer.
						}
					} while (n == 0);

					if (n == 0) break; // We're at EOF
				} // if (n == 0)

				// Got more chars in charBuffer than the user requested
				if (n > count)
					n = count;

				if (!readToUserBuffer)
				{
					Buffer.BlockCopy(_charBuffer, _charPos * 2, buffer, (index + charsRead) * 2, n * 2);
					_charPos += n;
				}

				charsRead += n;
				count -= n;

				// This function shouldn't block for an indefinite amount of time,
				// or reading from a network stream won't work right.  If we got
				// fewer bytes than we requested, then we want to break right here.
				if (_isBlocked)
					break;
			} // while (count > 0)

			return charsRead;
		}
		private void CompressBuffer(int n)
		{
			Buffer.BlockCopy(_byteBuffer, n, _byteBuffer, 0, _byteLen - n);
			_byteLen -= n;
		}

		private void DetectEncoding()
		{
			if (_byteLen < 2) return;
			_detectEncoding = false;
			var changedEncoding = false;
			if (_byteBuffer[0] == 0xFE && _byteBuffer[1] == 0xFF)
			{
				// Big Endian Unicode
				_encoding = new UnicodeEncoding(true, true);
				CompressBuffer(2);
				changedEncoding = true;
			}
			else if (_byteBuffer[0] == 0xFF && _byteBuffer[1] == 0xFE)
			{
				// Little Endian Unicode, or possibly little endian UTF32
				if (_byteLen < 4 || _byteBuffer[2] != 0 || _byteBuffer[3] != 0)
				{
					_encoding = new UnicodeEncoding(false, true);
					CompressBuffer(2);
					changedEncoding = true;
				}
				else
				{
					_encoding = new UTF32Encoding(false, true);
					CompressBuffer(4);
					changedEncoding = true;
				}
			}

			else if (_byteLen >= 3 && _byteBuffer[0] == 0xEF && _byteBuffer[1] == 0xBB && _byteBuffer[2] == 0xBF)
			{
				// UTF-8
				_encoding = Encoding.UTF8;
				CompressBuffer(3);
				changedEncoding = true;
			}
			else if (_byteLen >= 4 && _byteBuffer[0] == 0 && _byteBuffer[1] == 0 && _byteBuffer[2] == 0xFE && _byteBuffer[3] == 0xFF)
			{
				// Big Endian UTF32
				_encoding = new UTF32Encoding(true, true);
				CompressBuffer(4);
				changedEncoding = true;
			}
			else if (_byteLen == 2)
				_detectEncoding = true;
			// Note: in the future, if we change this algorithm significantly,
			// we can support checking for the preamble of the given encoding.

			if (!changedEncoding) return;
			_decoder = _encoding.GetDecoder();
			_maxCharsPerBuffer = _encoding.GetMaxCharCount(_byteBuffer.Length);
			_charBuffer = new char[_maxCharsPerBuffer];
		}

		// Trims the preamble bytes from the byteBuffer. This routine can be called multiple times
		// and we will buffer the bytes read until the preamble is matched or we determine that
		// there is no match. If there is no match, every byte read previously will be available
		// for further consumption. If there is a match, we will compress the buffer for the
		// leading preamble bytes
		private bool IsPreamble()
		{
			if (!_checkPreamble)
				return _checkPreamble;

			var len = (_byteLen >= (_preamble.Length)) ? (_preamble.Length - _bytePos) : (_byteLen - _bytePos);

			for (var i = 0; i < len; i++, _bytePos++)
			{
				if (_byteBuffer[_bytePos] == _preamble[_bytePos]) continue;
				_bytePos = 0;
				_checkPreamble = false;
				break;
			}

			if (!_checkPreamble) return _checkPreamble;
			if (_bytePos != _preamble.Length) return _checkPreamble;
			// We have a match
			CompressBuffer(_preamble.Length);
			_bytePos = 0;
			_checkPreamble = false;
			_detectEncoding = false;

			return _checkPreamble;
		}

		protected override void Dispose(bool disposing)
		{
			try {
				if (_closable && disposing) _stream?.Dispose();
			}
			finally {
				if (_closable && (_stream != null)) {
					_stream = null;
					_encoding = null;
					_decoder = null;
					_byteBuffer = null;
					_charBuffer = null;
					_charPos = 0;
					_charLen = 0;
					base.Dispose(disposing);
				}
			}
		}

	}
}
