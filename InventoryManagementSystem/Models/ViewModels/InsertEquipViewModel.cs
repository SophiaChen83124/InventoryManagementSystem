﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace InventoryManagementSystem.Models.ViewModels
{
    public class InsertEquipViewModel
    {
        public Guid EquipmentCategoryId { get; set; }
        public string EquipmentSn { get; set; }
        public string EquipmentName { get; set; }
        public string Brand { get; set; }
        public string Model { get; set; }
        public decimal UnitPrice { get; set; }
        public string Description { get; set; }
    }
}
