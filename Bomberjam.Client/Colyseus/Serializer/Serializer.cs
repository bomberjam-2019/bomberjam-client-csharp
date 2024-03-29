namespace Bomberjam.Client.Colyseus.Serializer
{
	internal interface ISerializer<T>
	{
		void SetState(byte[] data);
		T GetState();
		//IndexedDictionary<string, object> GetState();
		void Patch(byte[] data);

	    void Teardown ();
    	void Handshake (byte[] bytes, int offset);
	}
}
