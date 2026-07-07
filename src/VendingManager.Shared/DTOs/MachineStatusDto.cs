namespace VendingManager.Shared.DTOs
{
    public class MachineStatusDto
    {
        public string MachineId { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";    // "online", "warning", "offline"
        public int Noline { get; set; }              // 4=online, 2/1=warning, 0=offline
        public string Temperature { get; set; } = "";
        public string Door { get; set; } = "";       // "open" / "closed"
        public string CoinAcceptor { get; set; } = "";
        public string Selection { get; set; } = "";
        public string Version { get; set; } = "";
        public string Group { get; set; } = "";
    }

    public class MachineStatusResponse
    {
        public List<MachineStatusDto> Machines { get; set; } = new();
        public MachineStatusSummary? Summary { get; set; }
    }

    public class MachineStatusSummary
    {
        public string OnlineOffline { get; set; } = "";  // "4|1"
        public string DaySales { get; set; } = "";        // "71930|0|71930"
        public string Fault { get; set; } = "";
        public string OutOfStock { get; set; } = "";
    }
}
