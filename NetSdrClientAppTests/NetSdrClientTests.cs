using Moq;
using NetSdrClientApp;
using NetSdrClientApp.Networking;

namespace NetSdrClientAppTests;

public class NetSdrClientTests
{
    NetSdrClient _client;
    Mock<ITcpClient> _tcpMock;
    Mock<IUdpClient> _updMock;

    public NetSdrClientTests() { }

    [SetUp]
    public void Setup()
    {
        _tcpMock = new Mock<ITcpClient>();
        _tcpMock.Setup(tcp => tcp.Connect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(true);
        });

        _tcpMock.Setup(tcp => tcp.Disconnect()).Callback(() =>
        {
            _tcpMock.Setup(tcp => tcp.Connected).Returns(false);
        });

        _tcpMock.Setup(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>((bytes) =>
            {
                // емулюємо відповідь від TCP, щоб завершився TaskCompletionSource
                _tcpMock.Raise(tcp => tcp.MessageReceived += null, _tcpMock.Object, bytes);
            });

        _updMock = new Mock<IUdpClient>();

        _client = new NetSdrClient(_tcpMock.Object, _updMock.Object);
    }

    [Test]
    public async Task ConnectAsyncTest()
    {
        //act
        await _client.ConnectAsync();

        //assert
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3));
    }

    [Test]
    public async Task DisconnectWithNoConnectionTest()
    {
        //act
        _client.Disconect();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task DisconnectTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        _client.Disconect();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once);
    }

    [Test]
    public async Task StartIQNoConnectionTest()
    {

        //act
        await _client.StartIQAsync();

        //assert
        //No exception thrown
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        _tcpMock.VerifyGet(tcp => tcp.Connected, Times.AtLeastOnce);
    }

    [Test]
    public async Task StartIQTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        await _client.StartIQAsync();

        //assert
        //No exception thrown
        _updMock.Verify(udp => udp.StartListeningAsync(), Times.Once);
        Assert.That(_client.IQStarted, Is.True);
    }

    [Test]
    public async Task StopIQTest()
    {
        //Arrange 
        await ConnectAsyncTest();

        //act
        await _client.StopIQAsync();

        //assert
        //No exception thrown
        _updMock.Verify(tcp => tcp.StopListening(), Times.Once);
        Assert.That(_client.IQStarted, Is.False);
    }

    //TODO: cover the rest of the NetSdrClient code here

    [Test]
    public async Task ConnectAsync_WhenAlreadyConnected_DoesNothing()
    {
        // Arrange: емуляція вже активного з'єднання
        _tcpMock.Setup(tcp => tcp.Connected).Returns(true);

        // Act
        await _client.ConnectAsync();

        // Assert: не має бути повторного Connect та відправки pre-setup повідомлень
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Never);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
    }

    [Test]
    public async Task StopIQ_NoConnection_DoesNotSendOrStopUdp()
    {
        // Act
        await _client.StopIQAsync();

        // Assert
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        _updMock.Verify(udp => udp.StopListening(), Times.Never);
        Assert.That(_client.IQStarted, Is.False);
    }

    [Test]
    public async Task ChangeFrequencyAsync_NoConnection_DoesNotSend()
    {
        // Act
        await _client.ChangeFrequencyAsync(144800000L, 0);

        // Assert: при відсутності TCP-з'єднання внутрішній SendTcpRequest
        // має повернути null і не викликати SendMessageAsync
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Never);
        _tcpMock.VerifyGet(tcp => tcp.Connected, Times.AtLeastOnce);
    }

    [Test]
    public async Task ChangeFrequencyAsync_WithConnection_SendsMessage()
    {
        // Arrange: встановлюємо з'єднання (3 повідомлення pre-setup)
        await _client.ConnectAsync();

        // Act: міняємо частоту
        await _client.ChangeFrequencyAsync(144800000L, 1);

        // Assert:
        // 3 повідомлення при ConnectAsync + 1 при ChangeFrequencyAsync = 4
        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once);
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(4));
    }
    [Test]
    public async Task Disconnect_WhenIqStarted_OnlyDisconnectsTcp()
    {
        // Arrange
        await _client.ConnectAsync();
        await _client.StartIQAsync();

        _updMock.Invocations.Clear();
        _tcpMock.Invocations.Clear();

        // Act
        _client.Disconect();

        // Assert
        _tcpMock.Verify(tcp => tcp.Disconnect(), Times.Once,
            "Disconnect має викликати підʼєднаного TCP-клієнта.");
        _updMock.Verify(udp => udp.StopListening(), Times.Never,
            "Disconnect не торкається UDP-лісенера в поточній реалізації.");
        
    }


    [Test]
    public async Task StartIQ_AfterStopIQ_CanBeStartedAgain()
    {
        // Arrange
        await _client.ConnectAsync();
        await _client.StartIQAsync();
        await _client.StopIQAsync();

        _updMock.Invocations.Clear(); // щоб рахувати виклики тільки після StopIQ

        // Act
        await _client.StartIQAsync();

        // Assert
        _updMock.Verify(udp => udp.StartListeningAsync(), Times.Once,
            "IQ should be able to start again after StopIQAsync.");
        Assert.That(_client.IQStarted, Is.True);
    }

    [Test]
    public async Task ConnectAsync_Twice_DoesNotReconnectOrResendPreSetup()
    {
        // Arrange
        await _client.ConnectAsync();

        // Act
        await _client.ConnectAsync();


        _tcpMock.Verify(tcp => tcp.Connect(), Times.Once,
            "Connect should be performed only once.");
        _tcpMock.Verify(tcp => tcp.SendMessageAsync(It.IsAny<byte[]>()), Times.Exactly(3),
            "Pre-setup messages should be sent only once.");
    }

    [Test]
    public async Task StartIQ_WhenAlreadyStarted_RestartsUdpListener()
    {
        // Arrange
        await _client.ConnectAsync();
        await _client.StartIQAsync();
        Assert.That(_client.IQStarted, Is.True);

        _updMock.Invocations.Clear(); 

        // Act
        await _client.StartIQAsync(); 

        // Assert
        _updMock.Verify(udp => udp.StartListeningAsync(), Times.Once,
            "StartIQAsync викликає UDP-лісенер при повторному старті.");
        Assert.That(_client.IQStarted, Is.True);
    }

}
