﻿using System;
using System.Collections.Generic;

#nullable disable

namespace InventoryManagementSystem.Models
{
    public partial class Role
    {
        public Role()
        {
            Admins = new HashSet<Admin>();
        }

        public int RoleId { get; set; }
        public string RoleName { get; set; }

        public virtual ICollection<Admin> Admins { get; set; }
    }
}