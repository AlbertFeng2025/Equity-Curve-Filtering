#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO; 
using System.Linq;
using NinjaTrader.Cbi;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class TwoLayer_MasterContainer : Strategy
    {
        // =====================================================================
        // INTERNAL BRAIN (Do not change these)
        // =====================================================================
        private List<int> ghostOutcomes = new List<int>(); 
        private bool isArmedForRealTrade = false;
        private bool ghostTradeActive = false;
        private double ghostEntryPrice = 0;
        private int ghostTradeDirection = 0;
		private string logGhostResult = "-";
        private List<string> csvLines = new List<string>();
        private string filePath;
		private double lastTradePnL = 0; // Records the profit/loss of the trade that just finished
		private int barsSinceRealEntry = 0;
		

        // =====================================================================
        // SETTINGS YOU CAN CHANGE IN NINJATRADER UI
        // =====================================================================
        [NinjaScriptProperty]
        [Display(Name="Start Time (HHMMSS)", GroupName="1. Session", Order=1)]
        public int SessionStart { get; set; }

        [NinjaScriptProperty]
        [Display(Name="End Time (HHMMSS)", GroupName="1. Session", Order=2)]
        public int SessionEnd { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Filter Level (0 = All)", GroupName="2. Filter Logic", Order=1)]
        public int FilterLevel { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Run Type", GroupName="2. Filter Logic", Order=2)]
        public RunType FilterRunType { get; set; }

        [NinjaScriptProperty]
        [Display(Name="Export to CSV?", GroupName="3. Audit", Order=1)]
        public bool ExportCSV { get; set; }

        public enum RunType { UpRun, DownRun }

        // =====================================================================
        // INITIALIZATION
        // =====================================================================
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "TwoLayer_MasterContainer";
                Calculate = Calculate.OnBarClose;
                SessionStart = 093000;
                SessionEnd = 160000;
                FilterLevel = 3;
                FilterRunType = RunType.DownRun; 
                ExportCSV = true; 
            }
            else if (State == State.DataLoaded)
            {
                filePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "TwoLayerAudit_AnyStrategy.csv");
                csvLines.Add("Trade_Date,Entry_Time,Signal,Entry_Price,Exit_Price,Ghost_Result,New_Memory_String,Filter_Status,Real_Trade_Action,PnL_Points");
            }
            else if (State == State.Terminated)
            {
				// 1. Save the CSV
                if (ExportCSV && csvLines.Count > 1) 
                {
                    File.WriteAllLines(filePath, csvLines);
                    Print("Audit Log saved to: " + filePath);
                }
				// 2. The Real Math Summary
                Print("===============================================");
                Print("=== REAL PERFORMANCE: " + FilterRunType + " LEVEL " + FilterLevel + " ===");
                Print("===============================================");

                 
            }
        }

        // =====================================================================
        // CORE LOGIC ENGINE
        // =====================================================================
        protected override void OnBarUpdate()
        {
            if (CurrentBar < 20) return;

            string logRealTrade = "No";
			
            bool isInsideSession = (ToTime(Time[0]) >= SessionStart && ToTime(Time[0]) <= SessionEnd);
			// 0. Time Police: Reset everything at the end of the session
			if (ToTime(Time[0]) >= SessionEnd)
			{
			    if (Position.MarketPosition != MarketPosition.Flat)
			    {
			        ExitLong("TimeExit", "");
			        ExitShort("TimeExit", "");
			    }
			    isArmedForRealTrade = false; // Don't stay armed overnight
			    return; // Stop processing for the rest of this bar
			}

            // 1. Run the Ghost Logic
            UpdateGhostStrategy();

            // 2. Check for the UpRun/DownRun Pattern
            if (isInsideSession && !isArmedForRealTrade && ghostOutcomes.Count > 0)
            {
                if (CheckFilterPattern())
                {
                    isArmedForRealTrade = true;
                }
            }

            // 3. Take Real Trade if Armed
				int currentSignal = GetPrimarySignal();
				
				// --- ENTRY LOGIC ---
				if (isArmedForRealTrade && currentSignal != 0 && Position.MarketPosition == MarketPosition.Flat)
				{
				    if (currentSignal == 1) EnterLong("RealTrade");
				    if (currentSignal == -1) EnterShort("RealTrade");
				    
				    logRealTrade = "YES-" + (currentSignal == 1 ? "Long" : "Short");
				    isArmedForRealTrade = false; 
				    barsSinceRealEntry = 0; // Start the 10-minute timer
				}
				
				// --- EXIT LOGIC (The 10-minute / 1-bar Rule) ---
				if (Position.MarketPosition != MarketPosition.Flat)
				{
				    barsSinceRealEntry++;
				    
				    // If 1 bar (10 mins) has passed, exit now to match the Ghost logic
				    if (barsSinceRealEntry >= 1)
				    {
				        if (Position.MarketPosition == MarketPosition.Long) ExitLong("TimedExit", "RealTrade");
				        else ExitShort("TimedExit", "RealTrade");
				        
				        barsSinceRealEntry = 0; // Reset timer
				    }
				}

			// 4. Logging
            if (ExportCSV)
            {
                string memoryString = string.Join("", ghostOutcomes);
                
                string row = string.Format("{0},{1},{2},{3},{4},{5},{6},{7},{8}",
                    CurrentBar, 
                    Time[0].ToString("MM/dd/yyyy HH:mm:ss"), 
                    Close[0], 
                    currentSignal, 
                    logGhostResult, 
                    memoryString, 
                    isArmedForRealTrade ? "ARMED" : "WAITING", 
                    logRealTrade,
                    lastTradePnL != 0 ? lastTradePnL.ToString("F2") : "-"); // Shows points if trade finished, else "-"
                
                csvLines.Add(row);
                
                // Reset the accountant for the next bar
                lastTradePnL = 0;
                logGhostResult = "-";
            }
        }

        // =====================================================================
        // =====================================================================
        // >>> STRATEGY SWAP ZONE: START <<<
        // Change the logic inside these two boxes to try new strategies.
        // =====================================================================

        // BOX A: THE SIGNAL (What triggers a trade?)
        private int GetPrimarySignal()
        {
            // Example: Buy when 10 SMA crosses above 20 SMA
            if (CrossAbove(SMA(5), SMA(50), 1)) return 1;
            
            // Example: Sell when 10 SMA crosses below 20 SMA
           // if (CrossBelow(SMA(10), SMA(20), 1)) return -1;

            return 0; // Do nothing
        }

        // BOX B: THE GHOST RECORD (Did the hypothetical trade win?)
        private void UpdateGhostStrategy()
        {
            if (!ghostTradeActive)
            {
                int sig = GetPrimarySignal();
                if (sig != 0)
                {
                    ghostEntryPrice = Close[0];
                    ghostTradeDirection = sig;
                    ghostTradeActive = true;
                }
            }
           
			else 
            {
                // Calculate the actual points gained or lost
                // If Long: Close - Entry. If Short: Entry - Close.
                double pnlPoints = (ghostTradeDirection == 1) ? (Close[0] - ghostEntryPrice) : (ghostEntryPrice - Close[0]);
                
                // 1. Record the 1 or 0 for the memory string
                int outcome = (pnlPoints > 0) ? 1 : 0;
                ghostOutcomes.Add(outcome);
                
                // 2. Record the actual PnL for the CSV
                lastTradePnL = pnlPoints; 
                logGhostResult = outcome.ToString();
                
                ghostTradeActive = false;
            	
            }
        }

        // =====================================================================
        // >>> STRATEGY SWAP ZONE: END <<<
        // =====================================================================
        // =====================================================================


        // =====================================================================
        // PATTERN FILTER CALCULATOR (Matches your UpRun/DownRun logic)
        // =====================================================================
        private bool CheckFilterPattern()
        {
            if (FilterLevel == 0) return true; 
            if (ghostOutcomes.Count < FilterLevel + 1) return false;

            int targetVal = (FilterRunType == RunType.UpRun) ? 1 : 0;
            int anchorVal = (FilterRunType == RunType.UpRun) ? 0 : 1;

            // Check the streak length
            for (int i = 0; i < FilterLevel; i++)
            {
                if (ghostOutcomes[ghostOutcomes.Count - 1 - i] != targetVal) return false;
            }

            // Ensure it is anchored by the opposite result
            if (ghostOutcomes[ghostOutcomes.Count - 1 - FilterLevel] != anchorVal) return false;

            return true;
        }
    }
}