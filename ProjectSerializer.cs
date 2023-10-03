using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;

namespace Game
{
    public class ProjectSerializer
    {
       // To-do: Make a base class for these two
        public class Subsystem
        {
            public string Name { get; set; }
            
            private Dictionary<string,object> Values = new Dictionary<string,object>(); 
            
            public Subsystem(string name)
            {
                Name = name;
            }
            
            public void SetValue(string name, object value)
            {
                Values[name] = value;
            }
            
            public void AddListValue(string name, Dictionary<string,object> value)
            {
                if (!Values.ContainsKey(name))
                    Values[name] = new List<Dictionary<string,object>>();
                ((List<Dictionary<string,object>>) Values[name]).Add(value);
            }
            
            public void Setup(Dictionary<string,object> newValues)
            {
                foreach (KeyValuePair<string,object> pair in newValues)
                {
                    SetValue(pair.Key, pair.Value);
                }
            }
            
            public string ConvertType(Type type)
            {
                if (type.IsEnum)
                {
                    return "Game." + type.Name;
                }
                switch (type.Name)
                {
                    case "String":
                        return "string";
                    case "Int32":
                        return "int";
                    case "Int64":
                        return "long";
                    case "Single":
                        return "float";
                    case "Double":
                        return "double";
                    case "Boolean":
                        return "bool";
                }
                return type.Name;
            }
            
            public XElement Serialize(string name=null, Dictionary<string,object> ovalues=null)
            {
                if (ovalues == null || name == null)
                {
                    ovalues = Values;
                    name = Name;
                }
                
                XElement values = new XElement("Values", new XAttribute("Name", name));
                foreach (KeyValuePair<string,object> pair in ovalues)
                {
                    if (pair.Value is Dictionary<string,object> dict)
                    {
                        values.Add(Serialize(pair.Key, dict));
                    }
                    else if (pair.Value is List<Dictionary<string,object>> list)
                    {
                        Dictionary<string,object> reconstructed = new Dictionary<string,object>();
                        for (int i=1; i<=list.Count; i++)
                        {
                            reconstructed.Add(i.ToString(), list[i-1]);
                        }
                        values.Add(Serialize(pair.Key, reconstructed));
                    }
                    else
                    {
                        if (pair.Value != null)
                        {
                            values.Add(new XElement("Value", 
                                    new XAttribute("Name", pair.Key),
                                    new XAttribute("Value", pair.Value.ToString()),
                                    new XAttribute("Type", ConvertType(pair.Value.GetType()))
                             ));
                         }
                    }
                }
                return values;
            }
        }
        public class Component : Subsystem
        {
            public Component(string name) : base(name)
            {}
        }
        
        public class Entity
        {
            public string TemplateName { get; set; }
            public string Guid { get; set; }
            public int Id = 1;
            
            private List<Component> Components = new List<Component>(); 
            
            public Entity(string templateName, string guid)
            {
                TemplateName = templateName;
                Guid = guid;
            }
            
            public Component GetComponent(string name)
            {
                Component component = Components.FirstOrDefault((Component comp) => comp.Name == name);
                if (component != null)
                    return component;
                
                component = new Component(name);
                Components.Add(component);
                return component;
            }
            
            public XElement Serialize()
            {
                XElement values = new XElement("Entity", new XAttribute("Id", Id.ToString()), new XAttribute("Guid", Guid), new XAttribute("Name", TemplateName));
                foreach (Component component in Components)
                {
                    values.Add(component.Serialize());
                }
                return values;
            }
        }
        
        private List<Subsystem> Subsystems = new List<Subsystem>();
        private List<Entity> Entities = new List<Entity>();
        
        public Subsystem GetSubsystem(string subsystemName)
        {
            Subsystem subsystem = Subsystems.FirstOrDefault((Subsystem sbs) => sbs.Name == subsystemName);
            if (subsystem != null)
                return subsystem;
            
            subsystem = new Subsystem(subsystemName);
            Subsystems.Add(subsystem);
            return subsystem;
        }
        
        public Entity MakeEntity(string templateName, string guid)
        {
            Entity entity = new Entity(templateName, guid);
            entity.Id = Entities.Count+1;
            Entities.Add(entity);
            return entity;
        }
        
        public XElement Serialize()
        {
            XElement rootElement = new XElement("Project",
                new XAttribute("Origin", "Minecraft world"),
                new XAttribute("Converter", "Minecraft to Survivalcraft converter by NomeCriativoRFM"),
                new XAttribute("Guid", "9e9a67f8-79df-4d05-8cfa-61bd8095661e"),
                new XAttribute("Name", "GameProject"),
                new XAttribute("Version", "2.3")
            );
    
            
            XElement subsystems = new XElement("Subsystems");
            foreach (Subsystem subsystem in Subsystems)
            {
                subsystems.Add(subsystem.Serialize());
            }
            rootElement.Add(subsystems);
            
            XElement entities = new XElement("Entities");
            foreach (Entity entity in Entities)
            {
                entities.Add(entity.Serialize());
            }
            rootElement.Add(entities);
            
            return rootElement;
        }
        
        public void Save(string path)
        {
            File.WriteAllText(path, Serialize().ToString());
        }
    }
}